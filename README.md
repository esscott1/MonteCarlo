# Monte Carlo Retirement Simulator

## Primary Purpose

This application is, at its core, a retirement-readiness calculator that lets people explore their own path to financial freedom by simulating how a portfolio might hold up against years of withdrawals.

## Secondary purpose: illustrating Claude AI and CI/CD concepts

Beyond the retirement simulation itself, this repo doubles as a small, direct illustration of several Claude AI and CI/CD concepts, each implemented as simply as possible rather than abstracted into a reusable framework:

- **An end-to-end, human-in-the-loop change pipeline** — click the pencil icon on the Scenario runner page to submit a change request directly (currently scoped to changing the page's header and title). That request is composed into a Jira story by AI (see the tool-use bullet below), labelled with its type and moved straight to In Progress, which dispatches it to the headless CI/CD agent described below. The agent implements the change on a feature branch and the workflow opens a pull request for a human to review and merge — AI drives the request and the implementation, and the one human approval is merging the PR; Jira is the record of each request. The full sequence is in [How a change request becomes a pull request](#how-a-change-request-becomes-a-pull-request) below.
- **MCP (Model Context Protocol) via a Skill** — [.claude/skills/jira-commit/SKILL.md](.claude/skills/jira-commit/SKILL.md) defines a workflow that uses the Atlassian MCP server to comment on and transition Jira issues as part of committing and pushing a code change, linking the commit and the Jira issue back to each other in both directions.
- **Headless agents in CI/CD, routed by Jira label** — [.github/workflows/agent-dispatcher.yml](.github/workflows/agent-dispatcher.yml) receives a `repository_dispatch` event carrying a Jira story's key, summary, description and labels, and routes on the story's exact `agent-` label to a handler workflow for that type of change, such as [agent-title-change.yml](.github/workflows/agent-title-change.yml), which runs Claude Code non-interactively (`claude -p`). Shared steps around every handler create the feature branch, prove the handler stayed on it and in its scope, push it, open the pull request, and report back to Jira. See [Story types](#story-types).
- **Tool use in an application, not just tooling** — [MonteCarloSimulation.Web/ChangeRequestAgent.cs](MonteCarloSimulation.Web/ChangeRequestAgent.cs) calls the Anthropic API with a single forced tool call to compose a Jira story's fields from a visitor's change-request submission, and [MonteCarloSimulation.Web/JiraClient.cs](MonteCarloSimulation.Web/JiraClient.cs) is the only thing that actually writes to Jira (the model itself never reaches Jira directly). `Program.cs` wires the two together behind the `/api/change-request` endpoint, with the flow exposed to visitors through a flyout form in the web UI.

### How a change request becomes a pull request

The one human approval is **merging the pull request**. Jira only records each request: the app moves stories along by itself.

1. **A visitor submits the request.** On the Scenario runner page, the pencil icon opens a form for a summary, a description and the change-request passphrase. It posts to `POST /api/change-request`, which allows 5 requests per address over a rolling hour (`RequestQuota`) and checks the passphrase before doing anything else. After a refusal the form says exactly when the next request is allowed, and it shows how many are left after each one. *Uses:* `ChangeRequest__Passphrase` (Azure).
2. **AI composes the Jira story; the app files it.** [ChangeRequestAgent.cs](MonteCarloSimulation.Web/ChangeRequestAgent.cs) calls the Anthropic API with one forced tool call to compose the story's summary and description. The server verifies the result, and [JiraClient.cs](MonteCarloSimulation.Web/JiraClient.cs) creates the story in the `SCRUM` project with the label **`agent-title-change`** ([StoryLabels.cs](MonteCarloSimulation.Web/StoryLabels.cs)). The visitor sees the new issue key. *Uses:* `Anthropic__ApiKey`, `Jira__Email` and `Jira__ApiToken` (Azure).
3. **The app moves the story to In Progress** straight away (`JiraClient.TransitionAsync`). There's no triage step in Jira. If the move fails, the story and its key still exist, and moving it by hand starts the agent.
4. **A Jira Automation rule dispatches it.** The rule "Trigger AI Agent on In Progress" fires when a work item with a label beginning `agent-` moves to In Progress. It sends a web request to GitHub's repository dispatch endpoint, `POST https://api.github.com/repos/esscott1/MonteCarlo/dispatches`, with this payload:

   ```json
   {
     "event_type": "run_claude_agent",
     "client_payload": {
       "issue_key": "{{issue.key}}",
       "summary": "{{issue.summary.jsonEncode}}",
       "description": "{{issue.description.jsonEncode}}",
       "labels": {{issue.labels.asJsonStringArray}}
     }
   }
   ```

   `labels` is left unquoted: it renders a JSON array of the story's labels. If Jira rejects that function, `"labels": "{{issue.labels.join(\",\")}}"` (a comma-separated string) works too. The rule lives in Jira, not in this repo. *Uses:* a GitHub token stored in the rule's web-request settings.
5. **The dispatcher routes on the exact label.** [agent-dispatcher.yml](.github/workflows/agent-dispatcher.yml) checks the issue key is `SCRUM-<number>` and finds the story's `agent-` label ([resolve-story-type.mjs](.github/scripts/resolve-story-type.mjs)). The label must exactly match a handler in the [Story types](#story-types) table; a label ONLY starting with `agent-` isn't enough. A story with none, or with one that has no handler, ends here as a no-op (the run's summary says why); two `agent-` labels are refused as ambiguous.
6. **The handler makes the change on a feature branch.** For `agent-title-change`, [agent-title-change.yml](.github/workflows/agent-title-change.yml):
   - creates a branch `feature/<issue key>-agent-<yyyyMMdd-HHmm>` from `master` ([agent-prepare](.github/actions/agent-prepare/action.yml));
   - runs Claude Code non-interactively (`claude -p`, model `claude-sonnet-5`) with the handler's instructions ([title-change.md](.github/agent-prompts/title-change.md)) and the request. The request reaches the agent as data in its prompt, never as shell; the agent may only edit files, run `dotnet build`/`dotnet test`, and use git's `add`, `status`, `diff` and `commit`. It's told to work only on its branch, never to commit to `master`, and never to push or open pull requests.

   The agent changes the page's `<h1>` and `<title>`, builds, tests, and commits as `<issue key> Implement <summary>`. *Uses:* `ANTHROPIC_API_KEY` (GitHub secret).
7. **The workflow checks the work and opens the pull request** ([agent-finish](.github/actions/agent-finish/action.yml)). [check-handler-work.sh](.github/scripts/check-handler-work.sh) refuses to push unless the handler is still on its feature branch, the local `master` didn't move, everything is committed, and every changed file is in the handler's scope (for title changes, `MonteCarloSimulation.Web/wwwroot/*.html`), with nothing under `.github/`. The branch is pushed by an explicit refspec, so the push can only ever create that branch, and the PR is opened against `master`, titled `<issue key> <summary>`. The workflow then comments the PR link on the Jira story and moves the story to **In Review** ([jira-report.sh](.github/scripts/jira-report.sh)); if the run fails, it comments the run's link instead. [ci.yml](.github/workflows/ci.yml) runs the tests on the PR. *Uses:* `GITHUB_TOKEN` (automatic), and `JIRA_EMAIL` and `JIRA_API_TOKEN` (GitHub secrets).
8. **A person reviews and merges** — the only approval. Merging to `master` runs the deploy workflow below, which tests and publishes the site. *Uses:* `AZURE_WEBAPP_PUBLISH_PROFILE` (GitHub secret).

### Story types

| Label | Handler | Kind | May change | Branch |
|---|---|---|---|---|
| `agent-title-change` | [agent-title-change.yml](.github/workflows/agent-title-change.yml) | Claude Code, with [title-change.md](.github/agent-prompts/title-change.md) | `MonteCarloSimulation.Web/wwwroot/*.html` | `feature/<key>-agent-<yyyyMMdd-HHmm>` |

**Adding a type:** the label (in [StoryLabels.cs](MonteCarloSimulation.Web/StoryLabels.cs) if the app creates it), a handler workflow that runs `agent-prepare`, its change, then `agent-finish` with its allowed paths; the label in `HANDLERS` in [resolve-story-type.mjs](.github/scripts/resolve-story-type.mjs); one guarded job in the dispatcher; and a row here.

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
| `ChangeRequest__Passphrase` | Azure App Service → Environment variables | The web app: the change-request form (pencil) and the Observe passphrase | 1 | Chosen by the site owner; shared with whoever may submit change requests. |
| GitHub token | Jira → Project settings → Automation → "Trigger AI Agent on In Progress" → the web request's `Authorization` header | Jira, to send the `repository_dispatch` that starts the dispatcher | 4 | A GitHub personal access token allowed to trigger workflows on `esscott1/MonteCarlo`. Personal access tokens expire. |
| `ANTHROPIC_API_KEY` | GitHub → repo **Settings → Secrets and variables → Actions** | Claude Code in the AI handlers ([agent-title-change.yml](.github/workflows/agent-title-change.yml)) | 6 | Anthropic Console. **Same key as `Anthropic__ApiKey` above.** |
| `GITHUB_TOKEN` | Created by GitHub for each workflow run; nothing to store | The agent workflows, to push the branch and open the PR | 7 | GitHub Actions, automatically. |
| `JIRA_EMAIL` | GitHub → repo Settings → Secrets and variables → Actions | The agent workflows, to comment on the story and move it to In Review ([jira-report.sh](.github/scripts/jira-report.sh)) | 7 | The same Atlassian account email as `Jira__Email`. |
| `JIRA_API_TOKEN` | GitHub → repo Settings → Secrets and variables → Actions | The agent workflows, as above | 7 | An Atlassian API token for that account. **Renew it together with `Jira__ApiToken`** when they're the same token. |
| `AZURE_WEBAPP_PUBLISH_PROFILE` | GitHub → repo Settings → Secrets and variables → Actions | [deploy-azure.yml](.github/workflows/deploy-azure.yml), to deploy to the Web App | 7 | Azure portal → the Web App → **Download publish profile**. Replace it if the profile is reset. |

**How the web app finds its settings:** the app reads configuration keys like `Anthropic:ApiKey`. On Azure's Linux App Service, the colon becomes a double underscore in the environment variable's name, so that key is set as `Anthropic__ApiKey`. Non-secret Jira settings (`BaseUrl`, `ProjectKey`, `IssueType`) are committed in [appsettings.json](MonteCarloSimulation.Web/appsettings.json). If `Anthropic__ApiKey` or `Jira__ApiToken` is missing, the change-request form answers "Change requests are not configured on this server". If either is set but invalid or expired, it answers "The change request could not be completed".

**Running locally:** keep the same four web-app values in .NET user secrets (the project's `UserSecretsId` is in [MonteCarloSimulation.Web.csproj](MonteCarloSimulation.Web/MonteCarloSimulation.Web.csproj)), using the colon form, for example `dotnet user-secrets set "Anthropic:ApiKey" "<key>" --project MonteCarloSimulation.Web`.

**Separate from the app:** the Claude Code `jira-commit` skill reaches Jira through the Atlassian MCP connector, signed in through Claude, not through any of the secrets above.
