using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // Runs Core's simulation one market path at a time, for one investment scenario and one Social Security
    // claiming age. Path `i` always uses `new Random(i)`, so every spending level sees the same market paths
    // (common random numbers) - that's what makes survival monotone in spending and the search stable.
    //
    // It calls Core's own run loop (RunSimulator, the same one MonteCarloEngine.Run uses for each iteration) in its
    // lean mode, skipping the year-by-year report and the multi-run bookkeeping and failure-trace text the engine
    // builds, which this search doesn't need. Each path's returns are drawn once (RunSimulator.SeededReturns) and
    // can be shared with other simulators for the same scenario. Not thread-safe: each instance owns a private
    // parameter copy and changes its Withdrawal.
    internal sealed class PathSimulator
    {
        public const double Precision = 100;

        private readonly SimulationParameters _parameters;
        private readonly IReadOnlyList<RetirementYear> _timeline;
        private readonly IWithdrawalStrategy _strategy;
        private readonly ConversionCeiling? _ceiling;
        private readonly double _initialUpperBound;
        private readonly double[][] _paths;

        // `order` and `target` are the withdrawal order and conversion target to run: SpendingOptimizer resolves the
        // template's Automatic choices by trying the candidates. `paths` are the scenario's precomputed market paths
        // (SeededPaths); without them the simulator draws its own as paths are asked for.
        public PathSimulator(
            SimulationParameters template, InvestmentScenario scenario, DateOnly socialSecurityStart, double monthlyBenefit,
            WithdrawalStrategy order, RothConversionTarget target, double[][]? paths = null)
        {
            _parameters = ParametersCopy.Of(template);
            _parameters.Mean = scenario.Mean;
            _parameters.StdDev = scenario.StdDev;
            _parameters.ScenarioDescription = scenario.Description;
            _parameters.Iterations = 1;
            _parameters.SocialSecurityStartDate = socialSecurityStart;
            _parameters.SocialSecurityMonthlyAmount = monthlyBenefit;
            _parameters.WithdrawalStrategy = order;
            _parameters.RothConversionTarget = target;
            _ceiling = ConversionTargets.CeilingFor(target);

            // The timeline and strategy don't depend on the withdrawal, so they're built once.
            _timeline = RetirementTimeline.Build(_parameters);
            _strategy = WithdrawalStrategies.For(_parameters.WithdrawalStrategy);
            _paths = paths ?? [];

            // Every Social Security payment received over the retirement window, in actual (inflated) dollars -
            // the same per-year amount RunSimulator pays. It doesn't depend on markets, so it's the same on every path.
            TotalSocialSecurity = _timeline.Sum(year => monthlyBenefit * year.InflationFactor * year.SocialSecurityPayments);

            double assets = _parameters.InitialTaxableBalance + _parameters.InitialRothBasis + _parameters.InitialRothUnrealizedGain
                + _parameters.InitialBrokerageBasis + _parameters.InitialBrokerageUnrealizedGain + Math.Max(0, _parameters.NewMoney);
            _initialUpperBound = Math.Max(1_000, assets + 12 * monthlyBenefit * _parameters.Years);
        }

        public double TotalSocialSecurity { get; }

        // Market paths 0..count-1 for a scenario, drawn once and shared (read-only) by every simulator, spend, claiming
        // age and candidate that runs it. Only the template's retirement window decides how many years each holds.
        public static double[][] SeededPaths(SimulationParameters template, InvestmentScenario scenario, int count) =>
            RunSimulator.SeededReturns(scenario.Mean, scenario.StdDev, RetirementTimeline.Build(template).Count, count);

        public bool Survives(double annualWithdrawal, int path) => Probe(annualWithdrawal, path).Survived;

        // One path at one spend: whether it survives, and the after-tax money left if it does (for screening candidates).
        public (bool Survived, double AfterTaxLeft) Probe(double annualWithdrawal, int path)
        {
            _parameters.Withdrawal = annualWithdrawal;
            var returns = path < _paths.Length
                ? _paths[path]
                : RunSimulator.SeededReturns(_parameters.Mean, _parameters.StdDev, _timeline.Count, path, 1)[0];
            return RunSimulator.Survives(_parameters, _timeline, _strategy, returns, _ceiling, out var accounts)
                ? (true, accounts.AfterTaxValue)
                : (false, 0);
        }

        // The largest annual withdrawal (today's dollars, to within Precision) that `path` survives. A hint (another
        // candidate's break-even on the same path) narrows the starting bracket; the answer doesn't depend on it
        // beyond the Precision.
        public double BreakEven(int path, double? hint = null)
        {
            if (!Survives(0, path)) return 0;

            double lo, hi;
            if (hint is double h && h >= 2 * Precision)
            {
                lo = h * 0.95;
                hi = h * 1.05;
                // Walk the bracket down until `lo` survives and up until `hi` fails.
                while (!Survives(lo, path))
                {
                    hi = lo;
                    lo *= 0.8;
                    if (lo < Precision) { lo = 0; break; }
                }
                while (Survives(hi, path))
                {
                    lo = hi;
                    hi *= 1.25;
                    if (hi > 1e12) return lo;
                }
            }
            else
            {
                lo = 0;
                hi = _initialUpperBound;
                // Strong early returns can sustain more than the starting assets suggest: widen until the path fails.
                while (Survives(hi, path))
                {
                    lo = hi;
                    hi *= 2;
                    if (hi > 1e12) return lo;
                }
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
