namespace MonteCarloSimulation.Core
{
    // Resolves the app's Automatic choices for one set of inputs - the withdrawal order and how far to convert to
    // Roth - together. Every candidate pair runs on the same seeded market paths (path i = new Random(i)) at the
    // requested spending; the pair that survives the most paths wins, and a tie goes to the pair that leaves more
    // after-tax money at the end (Accounts.AfterTaxValue, so converting isn't penalized for paying tax early).
    // The Strategy Lab found no single order or conversion line best for every household, hence the per-household pick.
    internal static class AutomaticStrategy
    {
        public const int Paths = 200;

        public static readonly IReadOnlyList<WithdrawalStrategy> Orders = [WithdrawalStrategy.TaxOptimized, WithdrawalStrategy.ProRata];

        // Bracket12 first: today's line wins exact ties.
        public static readonly IReadOnlyList<RothConversionTarget> Targets =
            [RothConversionTarget.Bracket12, RothConversionTarget.Bracket22, RothConversionTarget.Bracket24, RothConversionTarget.None];

        // The pairs to try: Automatic expands to every candidate, an explicit choice stays as it is, and with
        // conversions switched off the only target is None.
        public static IReadOnlyList<(WithdrawalStrategy Order, RothConversionTarget Target)> Candidates(SimulationParameters parameters)
        {
            var orders = parameters.WithdrawalStrategy == WithdrawalStrategy.Automatic ? Orders : [parameters.WithdrawalStrategy];
            IReadOnlyList<RothConversionTarget> targets = !parameters.EnableRothConversions
                ? [RothConversionTarget.None]
                : parameters.RothConversionTarget == RothConversionTarget.Automatic ? Targets : [parameters.RothConversionTarget];
            return orders.SelectMany(o => targets.Select(t => (o, t))).ToList();
        }

        public static (WithdrawalStrategy Order, RothConversionTarget Target) Resolve(SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline)
        {
            var candidates = Candidates(parameters);
            if (candidates.Count == 1) return candidates[0];

            // Every candidate runs on the same paths, so draw them once
            var paths = RunSimulator.SeededReturns(parameters.Mean, parameters.StdDev, timeline.Count, Paths);
            var best = candidates[0];
            (int Survivors, double AfterTaxLeft) bestScore = (-1, 0);
            foreach (var candidate in candidates)
            {
                var score = Score(parameters, timeline, candidate.Order, candidate.Target, paths);
                if (score.Survivors > bestScore.Survivors
                    || (score.Survivors == bestScore.Survivors && score.AfterTaxLeft > bestScore.AfterTaxLeft))
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        // Paths survived at the requested spending, and the after-tax money left at the end across the surviving paths.
        internal static (int Survivors, double AfterTaxLeft) Score(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, WithdrawalStrategy order, RothConversionTarget target) =>
            Score(parameters, timeline, order, target, RunSimulator.SeededReturns(parameters.Mean, parameters.StdDev, timeline.Count, Paths));

        private static (int Survivors, double AfterTaxLeft) Score(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, WithdrawalStrategy order, RothConversionTarget target,
            double[][] paths)
        {
            var strategy = WithdrawalStrategies.For(order);
            var ceiling = ConversionTargets.CeilingFor(target);
            int survivors = 0;
            double afterTaxLeft = 0;
            foreach (var returns in paths)
            {
                if (!RunSimulator.Survives(parameters, timeline, strategy, returns, ceiling, out var accounts)) continue;
                survivors++;
                afterTaxLeft += accounts.AfterTaxValue;
            }
            return (survivors, afterTaxLeft);
        }
    }
}
