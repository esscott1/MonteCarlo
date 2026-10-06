using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // One situation from the single-year check: the Tax-optimized plan's tax against the lowest tax any Tax Deferred /
    // Brokerage split achieves for the same net need (Roth held at what the plan used - it's deliberately last).
    //  Gap = PlanTax - BestTax, in dollars. RothBeforeOthers flags a plan that used Roth while Tax Deferred or
    //  Brokerage still had money (would be a bug).
    internal sealed record SingleYearCase(
        int Id,
        double Need,
        double SsTaxable,
        double InflationFactor,
        bool AgeEligible,
        double EligibleTaxable,
        double Brokerage,
        double GainFraction,
        double EligibleRoth,
        double PlanGrossTaxable,
        double PlanGrossBrokerage,
        double PlanRoth,
        double PlanTax,
        double BestGrossTaxable,
        double BestGrossBrokerage,
        double BestTax,
        bool Shortfall,
        bool RothBeforeOthers)
    {
        public double Gap => PlanTax - BestTax;
    }

    internal static class SingleYearCheck
    {
        private const int GridPoints = 4_000;

        public static IReadOnlyList<SingleYearCase> Run(int count, int seed)
        {
            var strategy = new TaxOptimizedWithdrawalStrategy();
            return Enumerable.Range(0, count).Select(i => One(i, new Random(unchecked(seed * 7_919 + i)), strategy)).ToList();
        }

        private static SingleYearCase One(int id, Random rng, IWithdrawalStrategy strategy)
        {
            double U(double lo, double hi) => lo + (hi - lo) * rng.NextDouble();
            double LogU(double lo, double hi) => Math.Exp(U(Math.Log(lo), Math.Log(hi)));

            double inflation = U(1.0, 1.8);
            var taxYear = new TaxYear(ScenarioGenerator.StandardDeduction * inflation, inflation,
                FederalTaxBrackets.Single2026, FederalTaxBrackets.CapitalGainsSingle2026);

            double need = LogU(5_000, 300_000) * inflation;
            double ssTaxable = rng.NextDouble() < 0.4 ? 0 : U(0, 70_000) * inflation * TaxAssumptions.SsTaxableFraction;
            bool ageEligible = rng.NextDouble() < 0.8;
            double taxDeferred = rng.NextDouble() < 0.1 ? 0 : LogU(10_000, 3_000_000);
            double brokerage = rng.NextDouble() < 0.1 ? 0 : LogU(10_000, 3_000_000);
            double gainFraction = rng.NextDouble();
            double roth = rng.NextDouble() < 0.3 ? 0 : U(0, 500_000);
            double eligibleTaxable = ageEligible ? taxDeferred : 0;
            double eligibleRoth = ageEligible ? roth : roth * U(0.2, 1.0);

            var context = new WithdrawalContext(need, ssTaxable, taxYear, eligibleTaxable, brokerage, gainFraction, eligibleRoth);
            var plan = strategy.Plan(context);

            double ssTax = taxYear.OrdinaryTax(ssTaxable);
            double Tax(double grossTaxable, double sale) => taxYear.TotalTax(ssTaxable + grossTaxable, sale * gainFraction) - ssTax;
            double planTax = Tax(plan.GrossTaxable, plan.GrossBrokerage);

            bool rothBeforeOthers = plan.Roth > 0.01
                && (eligibleTaxable - plan.GrossTaxable > 1 || brokerage - plan.GrossBrokerage > 1);

            // Brute force: for each Tax Deferred draw g, Brokerage covers the rest of the need (exactly, by the
            // closed-form gross-up with its gains stacked on g). Keep the cheapest feasible split.
            double target = need - plan.Roth;
            double bestG = plan.GrossTaxable, bestS = plan.GrossBrokerage, bestTax = planTax;
            if (!plan.IsShortfall && target > 0)
            {
                double gHi = Math.Min(eligibleTaxable, taxYear.GrossUpOrdinary(target, ssTaxable));

                (double Sale, double Tax)? Split(double g)
                {
                    double netTaxable = g - taxYear.IncrementalOrdinaryTax(ssTaxable, g);
                    double sale = taxYear.GrossUpBrokerageSale(Math.Max(0, target - netTaxable), gainFraction, ssTaxable + g, 0);
                    if (sale > brokerage + 1e-6) return null;
                    return (sale, Tax(g, sale));
                }

                void Consider(double g)
                {
                    if (g < 0 || g > gHi) return;
                    if (Split(g) is var (sale, tax) && tax < bestTax - 1e-9)
                    {
                        bestG = g; bestS = sale; bestTax = tax;
                    }
                }

                for (int k = 0; k <= GridPoints; k++) Consider(gHi * k / GridPoints);

                // The tax is piecewise linear in g, so the minimum sits on a kink: every ordinary bracket edge and the
                // 0% capital gains ceiling, measured as Tax Deferred draws on top of Social Security.
                foreach (var bracket in taxYear.Brackets)
                    Consider(taxYear.StandardDeduction + bracket.LowerBound * inflation - ssTaxable);
                Consider(taxYear.StandardDeduction - ssTaxable);
                Consider(taxYear.ZeroRateCeilingGross - ssTaxable);

                // Then refine around the best point found (kinks where the sale's gains cross a bracket move with g).
                double step = gHi / GridPoints;
                double lo = Math.Max(0, bestG - step), hi = Math.Min(gHi, bestG + step);
                for (int round = 0; round < 60 && hi - lo > 1e-3; round++)
                {
                    double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
                    double t1 = Split(m1)?.Tax ?? double.PositiveInfinity, t2 = Split(m2)?.Tax ?? double.PositiveInfinity;
                    if (t1 <= t2) hi = m2; else lo = m1;
                    Consider(m1);
                    Consider(m2);
                }
            }

            return new SingleYearCase(id, need, ssTaxable, inflation, ageEligible, eligibleTaxable, brokerage, gainFraction, eligibleRoth,
                plan.GrossTaxable, plan.GrossBrokerage, plan.Roth, planTax, bestG, bestS, bestTax, plan.IsShortfall, rothBeforeOthers);
        }
    }
}
