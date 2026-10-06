namespace MonteCarloSimulation.Web
{
    // Labels the app puts on the Jira stories it creates. The Jira rule "Trigger AI Agent on In Progress" sends any story
    // with a label beginning "agent-" to GitHub when it moves to In Progress, and .github/workflows/agent-dispatcher.yml
    // routes on the exact label to that type's handler. A label here needs a matching entry in
    // .github/scripts/resolve-story-type.mjs (HANDLERS) and a handler workflow, or its stories end as a no-op.
    public static class StoryLabels
    {
        // A change request from the Scenario runner's pencil: change a page's <h1> and <title>
        public const string TitleChange = "agent-title-change";
    }
}
