# Monte Carlo Retirement Simulator

This application is, at its core, a retirement-readiness calculator that lets people explore their own path to financial freedom by simulating how a portfolio might hold up against years of withdrawals.

## Secondary purpose: illustrating Claude AI and CI/CD concepts

Beyond the retirement simulation itself, this repo doubles as a small, direct illustration of several Claude AI and CI/CD concepts, each implemented as simply as possible rather than abstracted into a reusable framework:

- **MCP (Model Context Protocol) via a Skill** — [.claude/skills/jira-commit/SKILL.md](.claude/skills/jira-commit/SKILL.md) defines a workflow that uses the Atlassian MCP server to comment on and transition Jira issues as part of committing and pushing a code change, linking the commit and the Jira issue back to each other in both directions.
- **A headless agent in CI/CD** — [.github/workflows/claude-agent.yml](.github/workflows/claude-agent.yml) runs Claude Code non-interactively (`claude -p`) inside a GitHub Actions workflow, triggered by a `repository_dispatch` event carrying a Jira issue's key, summary, and description. The agent implements the requested change on a fresh branch, commits it, and a following step pushes the branch and opens the pull request.
- **Tool use in an application, not just tooling** — [MonteCarloSimulation.Web/ChangeRequestAgent.cs](MonteCarloSimulation.Web/ChangeRequestAgent.cs) calls the Anthropic API with a single forced tool call to compose a Jira story's fields from a visitor's change-request submission, and [MonteCarloSimulation.Web/JiraClient.cs](MonteCarloSimulation.Web/JiraClient.cs) is the only thing that actually writes to Jira (the model itself never reaches Jira directly). `Program.cs` wires the two together behind the `/api/change-request` endpoint, with the flow exposed to visitors through a flyout form in the web UI.

It's worth being explicit: the tool-calling/AI round trip in the change-request flow isn't load-bearing for the application's actual purpose. It's included specifically to demonstrate the pattern — a real production application in this situation would most likely not choose to route a simple, deterministic string-composition task through an LLM call at all.
