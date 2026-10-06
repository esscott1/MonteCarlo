namespace MonteCarloSimulation.Core
{
    // The total balance across the runs on one date: the 10th percentile, the median and the 90th percentile.
    public sealed record BalanceBandPoint(DateOnly Date, double Lower, double Middle, double Upper);

    // The Scenario runner's chart: how the runs' balances spread out over time. The first point is the retirement
    // date with the starting balance; then one point at the end of each model year (Jan 1 of the next calendar year,
    // or the final anniversary). A run that has run out counts as $0 from its failure year on.
    internal static class BalanceBands
    {
        public const double LowerPercentile = 0.10;
        public const double MiddlePercentile = 0.50;
        public const double UpperPercentile = 0.90;

        public static IReadOnlyList<BalanceBandPoint> Build(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, IReadOnlyList<RunSummary> runs)
        {
            double start = Accounts.FromParameters(parameters).Total;
            var points = new List<BalanceBandPoint>(timeline.Count + 1) { new(parameters.RetirementDate, start, start, start) };
            var balances = new double[runs.Count];
            foreach (var year in timeline)
            {
                for (int run = 0; run < runs.Count; run++)
                {
                    var years = runs[run].Years;
                    balances[run] = year.Index < years.Count && !(runs[run].FailureYear <= year.Index)
                        ? Math.Max(0, years[year.Index].Balance)
                        : 0;
                }
                Array.Sort(balances);
                points.Add(new BalanceBandPoint(
                    year.End, Percentile(balances, LowerPercentile), Percentile(balances, MiddlePercentile), Percentile(balances, UpperPercentile)));
            }
            return points;
        }

        // The p-th percentile (0..1) of sorted values, interpolating linearly between the closest ranks (Excel's
        // PERCENTILE.INC): the median of an even count is the mean of the middle two.
        public static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            double rank = p * (sorted.Length - 1);
            int below = (int)Math.Floor(rank);
            int above = Math.Min(below + 1, sorted.Length - 1);
            return sorted[below] + (rank - below) * (sorted[above] - sorted[below]);
        }
    }
}
