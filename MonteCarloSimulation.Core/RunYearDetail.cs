namespace MonteCarloSimulation.Core
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
        double TaxableWithdrawalPercentOfBalance);
}
