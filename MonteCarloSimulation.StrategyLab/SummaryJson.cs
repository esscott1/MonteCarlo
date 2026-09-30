using System.Text.Json;

namespace MonteCarloSimulation.StrategyLab
{
    // How the lab's Summary is written as JSON, and where --publish puts it for the web app's Model Info page
    // (relative to the repository root, where the lab is normally run from).
    internal static class SummaryJson
    {
        public static readonly string PublishedPath =
            Path.Combine("MonteCarloSimulation.Web", "wwwroot", "model-info", "strategy-lab.json");

        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
