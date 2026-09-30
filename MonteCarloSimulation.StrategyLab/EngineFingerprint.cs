using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MonteCarloSimulation.StrategyLab
{
    // A short hash of what the engine does for a candidate set: a few fixed households, every combination, one
    // market path each, every year's full detail. It goes in the run folder's name, so resuming only ever reuses
    // results computed by the same engine - any change to Core's math (or its defaults) starts a fresh run.
    internal static class EngineFingerprint
    {
        public static string For(CandidateSet set)
        {
            var text = new StringBuilder(set.Name);
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                foreach (var scenario in ScenarioGenerator.Generate(3, seed: 1))
                    foreach (var combination in set.All)
                    {
                        var run = new PathRunner(scenario.Parameters, combination).Run(scenario.TotalAssets * 0.045, path: 0, out _);
                        foreach (var year in run.Years) text.Append(year);
                    }
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..8].ToLowerInvariant();
        }
    }
}
