// Frozen copy of the pre-refactor engine (master @ fa7101f), used only by EngineEquivalenceTests to prove
// the refactor changes no simulation result. Deleted once the refactor is complete - do not edit.
namespace MonteCarloSimulation.Core.Tests.Legacy
{
    public record RunYearDetail(
        int Year,
        double RateOfReturn,
        double ReturnAmount,
        double Withdrawal,
        double TaxableWithdrawal,
        double BrokerageWithdrawal,
        double RothWithdrawal,
        double TaxRate,
        double Balance,
        double TaxableBalance,
        double BrokerageBalance,
        double RothBalance,
        double OrdinaryTaxAmount,
        double CapitalGainsTaxAmount,
        double OrdinaryBracketRate,
        double? AmountUntilNextBracket,
        double? NextBracketRate,
        bool AgeEligible,
        double RothConversionAmount,
        double RothConversionTax,
        double AgeInYear,
        double TaxableWithdrawalPercentOfBalance,
        double SocialSecurityIncome,
        double SocialSecurityTax);
}

namespace MonteCarloSimulation.Core.Tests.Legacy
{
    public class SimulationResult
    {
        public double OutOfMoneyCount { get; set; }
        public List<int> YearsOutOfMoney { get; set; }
        public List<double> FailedScenarioAverages { get; set; }
        public List<double> SuccessMoneyRemaining { get; set; }
        public List<double> EndingBalances { get; set; }
        public List<double> AverageAnnualReturns { get; set; }
        public List<double> AverageTaxRates { get; set; }
        public List<double> LifetimeTaxesPaid { get; set; }
        public List<int?> FailureYears { get; set; }
        public List<int> HighestReturnYears { get; set; }
        public List<double> HighestReturnValues { get; set; }
        public List<int> LowestReturnYears { get; set; }
        public List<double> LowestReturnValues { get; set; }
        public List<int> LowestBalanceYears { get; set; }
        public List<double> LowestBalanceValues { get; set; }
        public List<List<RunYearDetail>> RunDetails { get; set; }
        // Add other result fields as needed
    }
}

namespace MonteCarloSimulation.Core.Tests.Legacy
{
    public class SimulationRunOutput
    {
        public required SimulationResult Result { get; init; }
        public required List<double> AllRates { get; init; }
        public required string OutOfMoneyMessage { get; init; }
        public List<double>? LastBalances { get; init; }
        public List<double>? LastAnnualReturns { get; init; }
        public List<double>? LastAnnualWithdrawals { get; init; }
        public List<double>? LastTaxableBalances { get; init; }
        public List<double>? LastBrokerageBalances { get; init; }
        public List<double>? LastRothBalances { get; init; }
        public List<double>? LastTaxableWithdrawals { get; init; }
        public List<double>? LastBrokerageWithdrawals { get; init; }
        public List<double>? LastRothWithdrawals { get; init; }
        public List<double>? LastTaxRates { get; init; }
        public List<double>? LastOrdinaryTaxAmounts { get; init; }
        public List<double>? LastCapitalGainsTaxAmounts { get; init; }
        public List<double>? LastOrdinaryBracketRates { get; init; }
        public List<double?>? LastAmountsUntilNextBracket { get; init; }
        public List<double?>? LastNextBracketRates { get; init; }
        public List<double>? LastRothConversionAmounts { get; init; }
        public List<double>? LastRothConversionTaxes { get; init; }
        public List<double>? LastAgesInYear { get; init; }
        public List<double>? LastTaxableWithdrawalPercents { get; init; }
        public List<double>? LastSocialSecurityIncomes { get; init; }
        public List<double>? LastSocialSecurityTaxes { get; init; }
    }
}
