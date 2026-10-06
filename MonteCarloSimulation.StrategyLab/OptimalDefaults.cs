using MonteCarloSimulation.Core;
using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.StrategyLab
{
    // The Optimal page's default inputs (optimal.html, its default asset mix included) run through the Optimal page's
    // own optimizer twice: with the behavior the app shipped before it chose its own accounts (Tax-optimized order,
    // conversions to the 12% line with their tax from Brokerage only) and with the app's current defaults. Shows on the
    // Model Info page what the change did to the Optimal page's recommendation.
    internal static class OptimalDefaults
    {
        // The Optimal page's default mix: 60/30/10 stocks/bonds/cash at 8%/19%, 4.5%/4% and 3.5%/1%, correlation 0.1.
        private static readonly AssetMix DefaultMix = new()
        {
            StockWeight = 0.6, BondWeight = 0.3, CashWeight = 0.1,
            StockMean = 0.08, StockStdDev = 0.19,
            BondMean = 0.045, BondStdDev = 0.04,
            CashMean = 0.035, CashStdDev = 0.01,
            StockBondCorrelation = 0.1
        };

        private const string DefaultMixDescription = "60% stocks / 30% bonds / 10% cash";

        public static OptimalDefaultsComparison Compare()
        {
            var before = Optimize(p =>
            {
                p.WithdrawalStrategy = WithdrawalStrategy.TaxOptimized;
                p.ConversionTaxFunding = ConversionTaxFunding.Brokerage;
                p.RothConversionTarget = RothConversionTarget.Bracket12;
            });
            var after = Optimize(_ => { });
            return new OptimalDefaultsComparison(DefaultMixDescription, Outcome(before.Optimum), Outcome(after.Optimum));
        }

        private static OptimizationResult Optimize(Action<SimulationParameters> behavior)
        {
            var template = new SimulationParameters
            {
                Years = 40,
                Iterations = 1,
                Birthdate = new DateOnly(1969, 7, 7),
                RetirementDate = new DateOnly(2027, 1, 1),
                InitialTaxableBalance = 1_000_000,
                InitialRothBasis = 15_000,
                InitialRothUnrealizedGain = 5_000,
                InitialBrokerageBasis = 100_000,
                InitialBrokerageUnrealizedGain = 300_000,
                NewMoney = 1_000_000,
                YearNewMoney = 10,
                AnnualStandardDeduction = 16_000,
                EnableRothConversions = true,
                Mean = DefaultMix.ExpectedReturn,
                StdDev = DefaultMix.StdDev,
                AssetMix = DefaultMix,
                ScenarioDescription = DefaultMixDescription
            };
            behavior(template);
            return SpendingOptimizer.Optimize(new OptimizationInputs
            {
                Template = template,
                SocialSecurity = new SocialSecurityCurve(2_750, 3_900, 4_800)
            });
        }

        private static OptimalOutcome Outcome(SpendOptimum s) => new(
            s.Recommended.Age, s.Recommended.SpendAt85, s.Recommended.SpendAtMidpoint, s.Recommended.SpendAt80, s.VerifiedSurvivalRate);
    }

    // Annual spends (today's dollars) at the recommended claiming age, and that age's re-simulated survival rate.
    internal sealed record OptimalOutcome(int RecommendedAge, double SpendAt85, double SpendAtMidpoint, double SpendAt80, double SurvivalRate);

    // The Optimal page's defaults before and after the app took over its account choices; Mix names the default mix.
    internal sealed record OptimalDefaultsComparison(string Mix, OptimalOutcome Before, OptimalOutcome After);
}
