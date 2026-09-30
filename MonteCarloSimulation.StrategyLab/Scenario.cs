using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // One household the lab tests every combination on. Parameters has everything but the Withdrawal (the lab
    // searches for it); the other fields are the features the report slices results by.
    internal sealed record Scenario(
        int Id,
        SimulationParameters Parameters,
        double RetirementAge,
        double TotalAssets,
        double TaxDeferredShare,
        double RothShare,
        double BrokerageGainFraction,
        int SocialSecurityStartAge,
        int InvestmentScenarioId)
    {
        public bool RetiresBefore59Half => RetirementAge < 59.5;

        // A field-by-field copy, so each PathRunner can change its own Withdrawal and conversion switch.
        public static SimulationParameters Copy(SimulationParameters p) => new()
        {
            Years = p.Years,
            Iterations = p.Iterations,
            Withdrawal = p.Withdrawal,
            Birthdate = p.Birthdate,
            RetirementDate = p.RetirementDate,
            InitialTaxableBalance = p.InitialTaxableBalance,
            InitialRothBasis = p.InitialRothBasis,
            InitialRothUnrealizedGain = p.InitialRothUnrealizedGain,
            InitialBrokerageBasis = p.InitialBrokerageBasis,
            InitialBrokerageUnrealizedGain = p.InitialBrokerageUnrealizedGain,
            Mean = p.Mean,
            StdDev = p.StdDev,
            NewMoney = p.NewMoney,
            YearNewMoney = p.YearNewMoney,
            SocialSecurityStartDate = p.SocialSecurityStartDate,
            SocialSecurityMonthlyAmount = p.SocialSecurityMonthlyAmount,
            AnnualStandardDeduction = p.AnnualStandardDeduction,
            EnableRothConversions = p.EnableRothConversions,
            ConversionTaxFunding = p.ConversionTaxFunding,
            RothConversionTarget = p.RothConversionTarget,
            WithdrawalStrategy = p.WithdrawalStrategy,
            ScenarioDescription = p.ScenarioDescription
        };
    }

    // Draws reproducible households over wide ranges from one seed: scenario i is the same for a given seed however
    // many are generated.
    internal static class ScenarioGenerator
    {
        public const double StandardDeduction = 16_000;

        public static IReadOnlyList<Scenario> Generate(int count, int seed) =>
            Enumerable.Range(0, count).Select(i => One(i, new Random(unchecked(seed * 1_000_003 + i)))).ToList();

        private static Scenario One(int id, Random rng)
        {
            double U(double lo, double hi) => lo + (hi - lo) * rng.NextDouble();

            // Timing: retire on a random day 2026-2032 at 50-70, for 20-40 years
            var retirementDate = new DateOnly(2026, 1, 1).AddDays(rng.Next(7 * 365));
            double retirementAge = U(50, 70);
            var birthdate = retirementDate.AddDays(-(int)Math.Round(retirementAge * 365.25));
            int years = rng.Next(20, 41);

            // Assets: $300k-$5M, log-uniform; Tax Deferred 0-100%, Roth 0-40% (capped by what's left), Brokerage the rest
            double assets = Math.Exp(U(Math.Log(300_000), Math.Log(5_000_000)));
            double taxDeferredShare = rng.NextDouble();
            double rothShare = U(0, Math.Min(0.4, 1 - taxDeferredShare));
            double brokerageShare = 1 - taxDeferredShare - rothShare;
            double rothBasisShare = U(0.2, 1.0);
            double gainFraction = U(0, 0.9);

            double roth = assets * rothShare;
            double brokerage = assets * brokerageShare;

            // Social Security: $0-$4,500 a month (none for 10% of households), first paid on a birthday 62-70
            int ssStartAge = rng.Next(62, 71);
            double ssMonthly = rng.NextDouble() < 0.1 ? 0 : U(0, 4_500);

            // Inheritance: none half the time, otherwise $50k-$1M in model year 0-15 (inside the window)
            bool inherits = rng.NextDouble() < 0.5;
            double newMoney = inherits ? U(50_000, 1_000_000) : 0;
            int yearNewMoney = inherits ? rng.Next(0, Math.Min(16, years)) : 0;

            var investment = InvestmentScenarios.All[rng.Next(InvestmentScenarios.All.Count)];

            var parameters = new SimulationParameters
            {
                Years = years,
                Iterations = 1,
                Withdrawal = 0,
                Birthdate = birthdate,
                RetirementDate = retirementDate,
                InitialTaxableBalance = assets * taxDeferredShare,
                InitialRothBasis = roth * rothBasisShare,
                InitialRothUnrealizedGain = roth * (1 - rothBasisShare),
                InitialBrokerageBasis = brokerage * (1 - gainFraction),
                InitialBrokerageUnrealizedGain = brokerage * gainFraction,
                Mean = investment.Mean,
                StdDev = investment.StdDev,
                NewMoney = newMoney,
                YearNewMoney = yearNewMoney,
                SocialSecurityStartDate = birthdate.AddYears(ssStartAge),
                SocialSecurityMonthlyAmount = ssMonthly,
                AnnualStandardDeduction = StandardDeduction,
                EnableRothConversions = false,
                WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
                ScenarioDescription = investment.Description
            };

            return new Scenario(id, parameters, retirementAge, assets, taxDeferredShare, rothShare, gainFraction, ssStartAge, investment.Id);
        }

        // The main page's default inputs (Retire On 2027-01-01, born 1970-01-01, 30 years, $950k Tax Deferred,
        // $15k/$5k Roth, $200k/$200k Brokerage, $1M inheritance in year 10, $2,750/month from the 62nd birthday),
        // under one investment scenario. Negative ids keep them apart from the generated set.
        public static Scenario PageDefault(InvestmentScenario investment)
        {
            var birthdate = new DateOnly(1970, 1, 1);
            var retirementDate = new DateOnly(2027, 1, 1);
            var parameters = new SimulationParameters
            {
                Years = 30,
                Iterations = 1,
                Withdrawal = 0,
                Birthdate = birthdate,
                RetirementDate = retirementDate,
                InitialTaxableBalance = 950_000,
                InitialRothBasis = 15_000,
                InitialRothUnrealizedGain = 5_000,
                InitialBrokerageBasis = 200_000,
                InitialBrokerageUnrealizedGain = 200_000,
                Mean = investment.Mean,
                StdDev = investment.StdDev,
                NewMoney = 1_000_000,
                YearNewMoney = 10,
                SocialSecurityStartDate = birthdate.AddYears(62),
                SocialSecurityMonthlyAmount = 2_750,
                AnnualStandardDeduction = StandardDeduction,
                EnableRothConversions = false,
                WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
                ScenarioDescription = investment.Description
            };
            double assets = 950_000 + 20_000 + 400_000;
            return new Scenario(-investment.Id, parameters, 57.0, assets, 950_000 / assets, 20_000 / assets, 0.5, 62, investment.Id);
        }
    }
}
