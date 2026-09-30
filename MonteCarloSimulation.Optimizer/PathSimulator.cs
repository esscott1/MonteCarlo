using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // Runs Core's simulation one market path at a time, for one investment scenario and one Social Security
    // claiming age. Path `i` always uses `new Random(i)`, so every spending level sees the same market paths
    // (common random numbers) - that's what makes survival monotone in spending and the search stable.
    //
    // It calls Core's own run loop (RunSimulator, the same one MonteCarloEngine.Run uses for each iteration)
    // directly, skipping the multi-run bookkeeping and failure-trace text the engine builds, which this search
    // doesn't need. Not thread-safe: each instance owns a private parameter copy and changes its Withdrawal.
    internal sealed class PathSimulator
    {
        public const double Precision = 100;

        private readonly SimulationParameters _parameters;
        private readonly IReadOnlyList<RetirementYear> _timeline;
        private readonly IWithdrawalStrategy _strategy;
        private readonly double _initialUpperBound;

        public PathSimulator(SimulationParameters template, InvestmentScenario scenario, DateOnly socialSecurityStart, double monthlyBenefit)
        {
            _parameters = ParametersCopy.Of(template);
            _parameters.Mean = scenario.Mean;
            _parameters.StdDev = scenario.StdDev;
            _parameters.ScenarioDescription = scenario.Description;
            _parameters.Iterations = 1;
            _parameters.SocialSecurityStartDate = socialSecurityStart;
            _parameters.SocialSecurityMonthlyAmount = monthlyBenefit;

            // The timeline and strategy don't depend on the withdrawal, so they're built once.
            _timeline = RetirementTimeline.Build(_parameters);
            _strategy = WithdrawalStrategies.For(_parameters.WithdrawalStrategy);

            double assets = _parameters.InitialTaxableBalance + _parameters.InitialRothBasis + _parameters.InitialRothUnrealizedGain
                + _parameters.InitialBrokerageBasis + _parameters.InitialBrokerageUnrealizedGain + Math.Max(0, _parameters.NewMoney);
            _initialUpperBound = Math.Max(1_000, assets + 12 * monthlyBenefit * _parameters.Years);
        }

        public bool Survives(double annualWithdrawal, int path)
        {
            _parameters.Withdrawal = annualWithdrawal;
            return !RunSimulator.Simulate(_parameters, _timeline, _strategy, new Random(path)).Failed;
        }

        // The largest annual withdrawal (today's dollars, to within Precision) that `path` survives.
        public double BreakEven(int path)
        {
            if (!Survives(0, path)) return 0;

            double lo = 0;
            double hi = _initialUpperBound;
            // Strong early returns can sustain more than the starting assets suggest: widen until the path fails.
            while (Survives(hi, path))
            {
                lo = hi;
                hi *= 2;
                if (hi > 1e12) return lo;
            }

            while (hi - lo > Precision)
            {
                double mid = lo + (hi - lo) / 2;
                if (Survives(mid, path)) lo = mid; else hi = mid;
            }
            return lo;
        }

        // For tests: the same path through Core's public-facing entry point (MonteCarloEngine.Run, one iteration).
        internal bool SurvivesViaEngine(double annualWithdrawal, int path)
        {
            _parameters.Withdrawal = annualWithdrawal;
            return MonteCarloEngine.Run(_parameters, new Random(path)).Result.OutOfMoneyCount == 0;
        }
    }
}
