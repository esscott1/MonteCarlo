# Monte Carlo Retirement Simulator

## Primary Purpose

This application is, at its core, a retirement-readiness calculator that lets people explore their own path to financial freedom by simulating how a portfolio might hold up against years of withdrawals.

## Secondary purpose: illustrating Claude AI and CI/CD concepts

Beyond the retirement simulation itself, this repo doubles as a small, direct illustration of several Claude AI and CI/CD concepts, each implemented as simply as possible rather than abstracted into a reusable framework:

- **An end-to-end, human-in-the-loop change pipeline** — click the pencil icon on the Scenario runner page to submit a change request directly (currently scoped to changing the page's header and title). That request is composed into a Jira story by AI (see the tool-use bullet below), filed through the standard Jira issue workflow, and then picked up by the headless CI/CD agent described below, which implements the change on a branch and opens a pull request for a human to review and merge — AI drives the request and the implementation, but a human stays in the loop at issue triage and PR review rather than changes landing automatically. The full sequence is in [How a change request becomes a pull request](#how-a-change-request-becomes-a-pull-request) below.
- **MCP (Model Context Protocol) via a Skill** — [.claude/skills/jira-commit/SKILL.md](.claude/skills/jira-commit/SKILL.md) defines a workflow that uses the Atlassian MCP server to comment on and transition Jira issues as part of committing and pushing a code change, linking the commit and the Jira issue back to each other in both directions.
- **A headless agent in CI/CD** — [.github/workflows/claude-agent.yml](.github/workflows/claude-agent.yml) runs Claude Code non-interactively (`claude -p`) inside a GitHub Actions workflow, triggered by a `repository_dispatch` event carrying a Jira issue's key, summary, and description. The agent implements the requested change on a fresh branch, commits it, and a following step pushes the branch and opens the pull request.
- **Tool use in an application, not just tooling** — [MonteCarloSimulation.Web/ChangeRequestAgent.cs](MonteCarloSimulation.Web/ChangeRequestAgent.cs) calls the Anthropic API with a single forced tool call to compose a Jira story's fields from a visitor's change-request submission, and [MonteCarloSimulation.Web/JiraClient.cs](MonteCarloSimulation.Web/JiraClient.cs) is the only thing that actually writes to Jira (the model itself never reaches Jira directly). `Program.cs` wires the two together behind the `/api/change-request` endpoint, with the flow exposed to visitors through a flyout form in the web UI.

### How a change request becomes a pull request

Two people stay in the loop: whoever triages the Jira story, and whoever reviews the pull request.

1. **A visitor submits the request.** On the Scenario runner page, the pencil icon opens a form for a summary, a description and the change-request passphrase. It posts to `POST /api/change-request`, which is rate-limited to 5 requests an hour per address and checks the passphrase before doing anything else. *Uses:* `ChangeRequest__Passphrase` (Azure).
2. **AI composes the Jira story; the app files it.** [ChangeRequestAgent.cs](MonteCarloSimulation.Web/ChangeRequestAgent.cs) calls the Anthropic API with one forced tool call to compose the story's summary and description. The server verifies the result, and [JiraClient.cs](MonteCarloSimulation.Web/JiraClient.cs) creates the story in the `SCRUM` project, in **To Do**. The visitor sees the new issue key. *Uses:* `Anthropic__ApiKey`, `Jira__Email` and `Jira__ApiToken` (Azure).
3. **A person triages it in Jira.** Nothing more happens until someone reviews the story and moves it to **In Progress**. That move is the approval to implement.
4. **A Jira Automation rule starts the agent.** The rule "Trigger AI Agent on In Progress" fires when a work item moves to In Progress. It sends a web request to GitHub's repository dispatch endpoint, `POST https://api.github.com/repos/esscott1/MonteCarlo/dispatches`, with this payload:

   ```json
   {
     "event_type": "run_claude_agent",
     "client_payload": {
       "issue_key": "{{issue.key}}",
       "summary": "{{issue.summary.jsonEncode}}",
       "description": "{{issue.description.jsonEncode}}"
     }
   }
   ```

   The rule lives in Jira, not in this repo. *Uses:* a GitHub token stored in the rule's web-request settings.
5. **GitHub runs the headless Claude agent.** The `run_claude_agent` event starts [.github/workflows/claude-agent.yml](.github/workflows/claude-agent.yml), which:
   - creates a branch `feature/<issue key>-agent-<yyyyMMdd-HHmm>`;
   - runs Claude Code non-interactively (`claude -p`, model `claude-sonnet-5`), passing it the issue key, summary and description;
   - limits the agent to editing files, `dotnet` commands and local git (`add`, `status`, `diff`, `commit`). It can't push or open pull requests.

   The agent makes the change, builds it, and commits it as `<issue key> Implement <summary>`. *Uses:* `ANTHROPIC_API_KEY` (GitHub secret).
6. **The workflow opens the pull request.** A separate step pushes the branch and opens a PR titled `<issue key> <summary>` against `master`, with the story's description in the body. *Uses:* `GITHUB_TOKEN`, which GitHub provides to every workflow run automatically.
7. **A person reviews and merges.** Merging to `master` runs the deploy workflow below, which tests and publishes the site. *Uses:* `AZURE_WEBAPP_PUBLISH_PROFILE` (GitHub secret).

Every key named here is described in [Keys and secrets](#keys-and-secrets).

It's worth being explicit: the tool-calling/AI round trip in the change-request flow isn't load-bearing for the application's actual purpose. It's included specifically to demonstrate the pattern — a real production application in this situation would most likely not choose to route a simple, deterministic string-composition task through an LLM call at all.

## Deployment

Every pull request runs the full test suite through [.github/workflows/ci.yml](.github/workflows/ci.yml), so it shows a pass/fail check before it's merged. That includes the pull requests opened by the change-request agent.

The web app deploys to Azure via [.github/workflows/deploy-azure.yml](.github/workflows/deploy-azure.yml), triggered on every push to `master` that touches `MonteCarloSimulation.Web/**`, `MonteCarloSimulation.Core/**`, or the workflow file itself:

1. Checks out the code and sets up .NET 10.
2. Runs `dotnet test MonteCarlo.sln --configuration Release` — tests must pass before anything is published.
3. Publishes `MonteCarloSimulation.Web` in Release configuration.
4. Deploys the published output via `azure/webapps-deploy@v3` to the Azure Web App `montecarlo-otsconsulting`, authenticating with a publish profile stored in the `AZURE_WEBAPP_PUBLISH_PROFILE` GitHub secret.

Provisioned resources: resource group `rg-montecarlo`, region `westus2`, Web App `montecarlo-otsconsulting` (Linux, .NET 10), live at `https://montecarlo.otsconsulting.ai/` (custom domain, set up per [docs/azure-custom-domain.md](docs/azure-custom-domain.md)) and `https://montecarlo-otsconsulting.azurewebsites.net/`.

Only `MonteCarloSimulation.Web` (which references `MonteCarloSimulation.Core` directly) ships to Azure — the console app, `MonteCarloSimulation1`, is a local dev tool only and is never deployed.

## Keys and secrets

No secret is committed to this repo. Each one lives in exactly one of the four places below, and **the Anthropic API key is stored in two of them**: when it's replaced or expires, update both.

| Secret | Stored in | Used by | Pipeline step | Where the value comes from |
|---|---|---|---|---|
| `Anthropic__ApiKey` | Azure App Service → `montecarlo-otsconsulting` → **Environment variables** | The web app, to compose change-request Jira stories ([ChangeRequestAgent.cs](MonteCarloSimulation.Web/ChangeRequestAgent.cs)) | 2 | An API key from the Anthropic Console (console.anthropic.com → API Keys). **Same key as `ANTHROPIC_API_KEY` below.** |
| `Jira__Email` | Azure App Service → Environment variables | The web app, to sign in to Jira ([JiraClient.cs](MonteCarloSimulation.Web/JiraClient.cs)) | 2 | The Atlassian account email the stories are created as. Not secret, but paired with the token. |
| `Jira__ApiToken` | Azure App Service → Environment variables | The web app, to create Jira stories | 2 | An Atlassian API token for that account (id.atlassian.com → Security → API tokens). Atlassian tokens expire. |
| `ChangeRequest__Passphrase` | Azure App Service → Environment variables | The web app: the change-request form (pencil) and the Observe page | 1 | Chosen by the site owner; shared with whoever may submit change requests. |
| GitHub token | Jira → Project settings → Automation → "Trigger AI Agent on In Progress" → the web request's `Authorization` header | Jira, to send the `repository_dispatch` that starts the agent | 4 | A GitHub personal access token allowed to trigger workflows on `esscott1/MonteCarlo`. Personal access tokens expire. |
| `ANTHROPIC_API_KEY` | GitHub → repo **Settings → Secrets and variables → Actions** | The headless Claude agent in [claude-agent.yml](.github/workflows/claude-agent.yml) | 5 | Anthropic Console. **Same key as `Anthropic__ApiKey` above.** |
| `GITHUB_TOKEN` | Created by GitHub for each workflow run; nothing to store | `claude-agent.yml`, to push the branch and open the PR | 6 | GitHub Actions, automatically. |
| `AZURE_WEBAPP_PUBLISH_PROFILE` | GitHub → repo Settings → Secrets and variables → Actions | [deploy-azure.yml](.github/workflows/deploy-azure.yml), to deploy to the Web App | 7 | Azure portal → the Web App → **Download publish profile**. Replace it if the profile is reset. |

**How the web app finds its settings:** the app reads configuration keys like `Anthropic:ApiKey`. On Azure's Linux App Service, the colon becomes a double underscore in the environment variable's name, so that key is set as `Anthropic__ApiKey`. Non-secret Jira settings (`BaseUrl`, `ProjectKey`, `IssueType`) are committed in [appsettings.json](MonteCarloSimulation.Web/appsettings.json). If `Anthropic__ApiKey` or `Jira__ApiToken` is missing, the change-request form answers "Change requests are not configured on this server". If either is set but invalid or expired, it answers "The change request could not be completed".

**Running locally:** keep the same four web-app values in .NET user secrets (the project's `UserSecretsId` is in [MonteCarloSimulation.Web.csproj](MonteCarloSimulation.Web/MonteCarloSimulation.Web.csproj)), using the colon form, for example `dotnet user-secrets set "Anthropic:ApiKey" "<key>" --project MonteCarloSimulation.Web`.

**Separate from the app:** the Claude Code `jira-commit` skill reaches Jira through the Atlassian MCP connector, signed in through Claude, not through any of the secrets above.
