namespace MonteCarloSimulation.StrategyLab
{
    // One combination's results on one scenario.
    //  Spend825 / Spend50: the most annual spending (today's dollars) that 82.5% / 50% of the paths survive.
    //  CommonSpend: the baseline's Spend825 - every combination is also run at this one spend, so its taxes and
    //  leftover wealth compare like for like: SurvivalAtCommon, and the medians over paths of lifetime taxes and
    //  after-tax ending wealth (today's dollars, 0 for a failed path).
    internal sealed record ResultRow(
        int ScenarioId,
        string Combination,
        double Spend825,
        double Spend50,
        double CommonSpend,
        double SurvivalAtCommon,
        double MedianTaxesAtCommon,
        double MedianAfterTaxWealthAtCommon);

    internal static class ScenarioEvaluator
    {
        public const double TargetSurvival = 0.825;

        public static IReadOnlyList<ResultRow> Evaluate(Scenario scenario, IReadOnlyList<Combination> combinations, int paths)
        {
            // The baseline (today's default) is searched cold; its break-evens then seed every other combination's
            // search on the same path.
            var baseline = combinations[0];
            var baselineRunner = new PathRunner(scenario.Parameters, baseline);
            var baselineBreakEvens = Enumerable.Range(0, paths).Select(path => baselineRunner.BreakEven(path)).ToArray();
            double commonSpend = SpendAtSurvival(Sorted(baselineBreakEvens), TargetSurvival);

            var rows = new List<ResultRow>(combinations.Count);
            foreach (var combination in combinations)
            {
                var runner = combination == baseline ? baselineRunner : new PathRunner(scenario.Parameters, combination);
                var breakEvens = combination == baseline
                    ? baselineBreakEvens
                    : Enumerable.Range(0, paths).Select(path => runner.BreakEven(path, baselineBreakEvens[path])).ToArray();
                var sorted = Sorted(breakEvens);

                var outcomes = Enumerable.Range(0, paths).Select(path => runner.Outcome(commonSpend, path)).ToList();

                rows.Add(new ResultRow(
                    scenario.Id,
                    combination.Code,
                    SpendAtSurvival(sorted, TargetSurvival),
                    SpendAtSurvival(sorted, 0.5),
                    commonSpend,
                    outcomes.Count(o => o.Survived) / (double)paths,
                    Median(outcomes.Select(o => o.LifetimeTaxes)),
                    Median(outcomes.Select(o => o.AfterTaxEndingWealth))));
            }
            return rows;
        }

        // The largest spend that at least `survival` of the paths survive, given their break-evens sorted ascending
        // (the Optimal page's SpendingOptimizer.SpendAtSurvival).
        public static double SpendAtSurvival(double[] sortedBreakEvens, double survival)
        {
            int n = sortedBreakEvens.Length;
            int survivorsNeeded = (int)Math.Ceiling(survival * n - 1e-9);
            int index = Math.Clamp(n - survivorsNeeded, 0, n - 1);
            return sortedBreakEvens[index];
        }

        public static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return 0;
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        private static double[] Sorted(double[] values)
        {
            var copy = (double[])values.Clone();
            Array.Sort(copy);
            return copy;
        }
    }
}
