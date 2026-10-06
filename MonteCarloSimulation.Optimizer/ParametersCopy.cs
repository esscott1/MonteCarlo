using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    internal static class ParametersCopy
    {
        // A field-by-field copy, so each PathSimulator can change its own Withdrawal and Social Security fields.
        // (A test checks that every settable SimulationParameters property is copied.)
        public static SimulationParameters Of(SimulationParameters p) => new()
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
            AssetMix = p.AssetMix,
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
}
