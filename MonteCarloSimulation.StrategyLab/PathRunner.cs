using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // Runs Core's own per-run loop (RunSimulator) one seeded market path at a time for one scenario and one
    // combination - the Optimal page's method (PathSimulator), copied here so the Optimizer isn't touched. Path `i`
    // always uses `new Random(i)`, so every combination and spend sees the same markets. Not thread-safe: each
    // instance owns a private parameter copy and changes its Withdrawal.
    internal sealed class PathRunner
    {
        public const double Precision = 100;

        // After-tax value of what's left at the end: Tax Deferred still owes income tax, Brokerage gains still owe
        // capital gains tax - so a combination isn't credited for leaving untaxed money behind.
        public const double TaxDeferredAfterTax = 0.78;
        public const double GainsAfterTax = 0.85;

        private readonly SimulationParameters _parameters;
        private readonly IReadOnlyList<RetirementYear> _timeline;
        private readonly IWithdrawalStrategy _strategy;
        private readonly ConversionCeiling _ceiling;
        private readonly double _initialUpperBound;
        private readonly double _finalInflation;

        public PathRunner(SimulationParameters scenario, Combination combination)
        {
            _parameters = Scenario.Copy(scenario);
            _parameters.Iterations = 1;
            _parameters.EnableRothConversions = combination.Policy.Converts;
            _ceiling = combination.Policy.Ceiling ?? RothConversion.DefaultCeiling;
            _strategy = combination.Order.Strategy;
            _timeline = RetirementTimeline.Build(_parameters);
            _finalInflation = _timeline[^1].InflationFactor;

            double assets = _parameters.InitialTaxableBalance + _parameters.InitialRothBasis + _parameters.InitialRothUnrealizedGain
                + _parameters.InitialBrokerageBasis + _parameters.InitialBrokerageUnrealizedGain + Math.Max(0, _parameters.NewMoney);
            _initialUpperBound = Math.Max(1_000, assets + 12 * _parameters.SocialSecurityMonthlyAmount * _parameters.Years);
        }

        public bool Survives(double annualWithdrawal, int path) => !Run(annualWithdrawal, path, out _).Failed;

        public RunSummary Run(double annualWithdrawal, int path, out Accounts finalAccounts)
        {
            _parameters.Withdrawal = annualWithdrawal;
            return RunSimulator.Simulate(_parameters, _timeline, _strategy, new Random(path), _ceiling, out finalAccounts);
        }

        // One path at a fixed spend: survival, lifetime taxes, and after-tax ending wealth in today's dollars (0 if it failed).
        public PathOutcome Outcome(double annualWithdrawal, int path)
        {
            var run = Run(annualWithdrawal, path, out var a);
            double afterTax = run.Failed
                ? 0
                : (a.Taxable * TaxDeferredAfterTax + a.Brokerage - Math.Max(0, a.Brokerage - a.BrokerageBasis) * (1 - GainsAfterTax) + a.Roth)
                  / _finalInflation;
            return new PathOutcome(!run.Failed, run.LifetimeTaxesPaid, afterTax);
        }

        // The largest annual withdrawal (today's dollars, to within Precision) that `path` survives. A hint (another
        // combination's break-even on the same path) narrows the starting bracket; the answer doesn't depend on it
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
    }

    internal readonly record struct PathOutcome(bool Survived, double LifetimeTaxes, double AfterTaxEndingWealth);
}
