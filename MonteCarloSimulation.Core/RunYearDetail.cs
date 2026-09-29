namespace MonteCarloSimulation.Core
{
    // One simulated year of one run. Built once per year by the engine and never mutated afterwards.
    public record RunYearDetail
    {
        public required int Year { get; init; }
        public required double RateOfReturn { get; init; }
        public required double ReturnAmount { get; init; }
        public required double Withdrawal { get; init; }
        public required double TaxableWithdrawal { get; init; }
        public required double BrokerageWithdrawal { get; init; }
        public required double RothWithdrawal { get; init; }
        public required double TaxRate { get; init; }
        public required double Balance { get; init; }
        public required double TaxableBalance { get; init; }
        public required double BrokerageBalance { get; init; }
        public required double RothBalance { get; init; }
        public required double OrdinaryTaxAmount { get; init; }
        public required double CapitalGainsTaxAmount { get; init; }
        public required double OrdinaryBracketRate { get; init; }
        public required double? AmountUntilNextBracket { get; init; }
        public required double? NextBracketRate { get; init; }
        public required bool AgeEligible { get; init; }
        public required double RothConversionAmount { get; init; }
        public required double RothConversionTax { get; init; }
        public required double AgeInYear { get; init; }
        public required double TaxableWithdrawalPercentOfBalance { get; init; }
        public required double SocialSecurityIncome { get; init; }
        public required double SocialSecurityTax { get; init; }
    }
}
