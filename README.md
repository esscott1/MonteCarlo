# Monte Carlo Retirement Simulator

## Primary Purpose

This application is, at its core, a retirement-readiness calculator that lets people explore their own path to financial freedom by simulating how a portfolio might hold up against years of withdrawals.

## Secondary purpose: illustrating Claude AI and CI/CD concepts

Beyond the retirement simulation itself, this repo doubles as a small, direct illustration of several Claude AI and CI/CD concepts, each implemented as simply as possible rather than abstracted into a reusable framework:

- **An end-to-end, human-in-the-loop change pipeline** — click the pencil icon on the Scenario runner page to submit a change request directly (currently scoped to changing the page's header and title). That request is composed into a Jira story by AI (see the tool-use bullet below), filed through the standard Jira issue workflow, and then picked up by the headless CI/CD agent described below, which implements the change on a branch and opens a pull request for a human to review and merge — AI drives the request and the implementation, but a human stays in the loop at issue triage and PR review rather than changes landing automatically.
- **MCP (Model Context Protocol) via a Skill** — [.claude/skills/jira-commit/SKILL.md](.claude/skills/jira-commit/SKILL.md) defines a workflow that uses the Atlassian MCP server to comment on and transition Jira issues as part of committing and pushing a code change, linking the commit and the Jira issue back to each other in both directions.
- **A headless agent in CI/CD** — [.github/workflows/claude-agent.yml](.github/workflows/claude-agent.yml) runs Claude Code non-interactively (`claude -p`) inside a GitHub Actions workflow, triggered by a `repository_dispatch` event carrying a Jira issue's key, summary, and description. The agent implements the requested change on a fresh branch, commits it, and a following step pushes the branch and opens the pull request.
- **Tool use in an application, not just tooling** — [MonteCarloSimulation.Web/ChangeRequestAgent.cs](MonteCarloSimulation.Web/ChangeRequestAgent.cs) calls the Anthropic API with a single forced tool call to compose a Jira story's fields from a visitor's change-request submission, and [MonteCarloSimulation.Web/JiraClient.cs](MonteCarloSimulation.Web/JiraClient.cs) is the only thing that actually writes to Jira (the model itself never reaches Jira directly). `Program.cs` wires the two together behind the `/api/change-request` endpoint, with the flow exposed to visitors through a flyout form in the web UI.

It's worth being explicit: the tool-calling/AI round trip in the change-request flow isn't load-bearing for the application's actual purpose. It's included specifically to demonstrate the pattern — a real production application in this situation would most likely not choose to route a simple, deterministic string-composition task through an LLM call at all.

## Deployment

The web app deploys to Azure via [.github/workflows/deploy-azure.yml](.github/workflows/deploy-azure.yml), triggered on every push to `master` that touches `MonteCarloSimulation.Web/**`, `MonteCarloSimulation.Core/**`, or the workflow file itself:

1. Checks out the code and sets up .NET 10.
2. Runs `dotnet test MonteCarlo.sln --configuration Release` — tests must pass before anything is published.
3. Publishes `MonteCarloSimulation.Web` in Release configuration.
4. Deploys the published output via `azure/webapps-deploy@v3` to the Azure Web App `montecarlo-otsconsulting`, authenticating with a publish profile stored in the `AZURE_WEBAPP_PUBLISH_PROFILE` GitHub secret.

Provisioned resources: resource group `rg-montecarlo`, region `westus2`, Web App `montecarlo-otsconsulting` (Linux, .NET 10), live at `https://montecarlo-otsconsulting.azurewebsites.net/`. A custom domain hasn't been set up yet; [docs/azure-custom-domain.md](docs/azure-custom-domain.md) describes the steps for when it is.

Only `MonteCarloSimulation.Web` (which references `MonteCarloSimulation.Core` directly) ships to Azure — the console app, `MonteCarloSimulation1`, is a local dev tool only and is never deployed.
