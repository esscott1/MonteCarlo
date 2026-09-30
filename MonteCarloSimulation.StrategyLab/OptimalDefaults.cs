using MonteCarloSimulation.Core;
using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.StrategyLab
{
    // The Optimal page's default inputs (optimal.html) run through the Optimal page's own optimizer twice: with the
    // behavior the app shipped before it chose its own accounts (Tax-optimized order, conversions to the 12% line
    // with their tax from Brokerage only) and with the app's current defaults. Shows on the Model Info page what the change did to the Optimal
    // page's recommendations.
    internal static class OptimalDefaults
    {
        public static List<OptimalDefaultsRow> Compare()
        {
            var before = Optimize(p =>
            {
                p.WithdrawalStrategy = WithdrawalStrategy.TaxOptimized;
                p.ConversionTaxFunding = ConversionTaxFunding.Brokerage;
                p.RothConversionTarget = RothConversionTarget.Bracket12;
            });
            var after = Optimize(_ => { });
            return before.Scenarios.Zip(after.Scenarios, (b, a) =>
                new OptimalDefaultsRow(b.ScenarioId, b.Description, Outcome(b), Outcome(a))).ToList();
        }

        private static OptimizationResult Optimize(Action<SimulationParameters> behavior)
        {
            var template = new SimulationParameters
            {
                Years = 30,
                Iterations = 1,
                Birthdate = new DateOnly(1970, 1, 1),
                RetirementDate = new DateOnly(2027, 1, 1),
                InitialTaxableBalance = 950_000,
                InitialRothBasis = 15_000,
                InitialRothUnrealizedGain = 5_000,
                InitialBrokerageBasis = 200_000,
                InitialBrokerageUnrealizedGain = 200_000,
                NewMoney = 1_000_000,
                YearNewMoney = 10,
                AnnualStandardDeduction = 16_000,
                EnableRothConversions = true,
                ScenarioDescription = "Optimal"
            };
            behavior(template);
            return SpendingOptimizer.Optimize(new OptimizationInputs
            {
                Template = template,
                SocialSecurity = new SocialSecurityCurve(2_750, 3_900, 4_800)
            });
        }

        private static OptimalOutcome Outcome(ScenarioOptimum s) => new(
            s.Recommended.Age, s.Recommended.SpendAt85, s.Recommended.SpendAtMidpoint, s.Recommended.SpendAt80, s.VerifiedSurvivalRate);
    }

    // Annual spends (today's dollars) at the recommended claiming age, and that age's re-simulated survival rate.
    internal sealed record OptimalOutcome(int RecommendedAge, double SpendAt85, double SpendAtMidpoint, double SpendAt80, double SurvivalRate);

    internal sealed record OptimalDefaultsRow(int ScenarioId, string Description, OptimalOutcome Before, OptimalOutcome After);
}
