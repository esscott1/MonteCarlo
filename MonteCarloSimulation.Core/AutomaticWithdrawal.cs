namespace MonteCarloSimulation.Core
{
    // Resolves WithdrawalStrategy.Automatic for one set of inputs: runs each candidate order on the same seeded
    // market paths (path i = new Random(i)) at the requested spending and picks the one that survives more of them.
    // A tie - both survive every path, or the same number - goes to the order that leaves more money at the end,
    // the one with more headroom. The Strategy Lab found neither order best for every household (Pro-rata more
    // robust, Tax-optimized ahead for some), hence the per-household pick.
    internal static class AutomaticWithdrawal
    {
        public const int Paths = 200;

        public static readonly IReadOnlyList<WithdrawalStrategy> Candidates = [WithdrawalStrategy.TaxOptimized, WithdrawalStrategy.ProRata];

        public static WithdrawalStrategy Resolve(SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline)
        {
            if (parameters.WithdrawalStrategy != WithdrawalStrategy.Automatic) return parameters.WithdrawalStrategy;

            var best = Candidates[0];
            (int Survivors, double EndingMoney) bestScore = (-1, 0);
            foreach (var candidate in Candidates)
            {
                var score = Score(parameters, timeline, candidate);
                if (score.Survivors > bestScore.Survivors
                    || (score.Survivors == bestScore.Survivors && score.EndingMoney > bestScore.EndingMoney))
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        // Paths survived at the requested spending, and the total left at the end across all paths (0 for a failed one).
        internal static (int Survivors, double EndingMoney) Score(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, WithdrawalStrategy candidate)
        {
            var strategy = WithdrawalStrategies.For(candidate);
            int survivors = 0;
            double endingMoney = 0;
            for (int path = 0; path < Paths; path++)
            {
                var run = RunSimulator.Simulate(parameters, timeline, strategy, new Random(path));
                if (run.Failed) continue;
                survivors++;
                endingMoney += run.EndingBalance;
            }
            return (survivors, endingMoney);
        }
    }
}
