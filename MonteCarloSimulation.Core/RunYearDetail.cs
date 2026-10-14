namespace MonteCarloSimulation.Core
{
    // One simulated year of one run. Built once per year by the engine and never mutated afterwards.
    public record RunYearDetail
    {
        // Model year index (0 = the retirement year) and the calendar (tax) year it is. YearFraction is the share of
        // the calendar year inside the retirement window - below 1 for a partial first or last year.
        public required int Year { get; init; }
        public required int CalendarYear { get; init; }
        public required double YearFraction { get; init; }
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
        // Marginal long-term capital gains bracket (0, 0.15, 0.20) at the top of the year's income stack - the
        // rate the next dollar of gain would face - and the gain dollars left before the next LTCG bracket.
        public required double CapitalGainsBracketRate { get; init; }
        public required double? AmountUntilNextCapitalGainsBracket { get; init; }
        public required double? NextCapitalGainsBracketRate { get; init; }
        // Gains realized at 0% by selling and immediately rebuying Brokerage holdings (a basis step-up; no cash moves)
        public required double HarvestedGains { get; init; }
        // Embedded gain realized by this year's Brokerage sales (spending and conversion-tax sales); the rest of
        // BrokerageWithdrawal was a tax-free return of basis. Reporting only.
        public required double RealizedGains { get; init; }
        // Gain dollars (realized plus harvested) taxed at 0% this year. Reporting only.
        public required double ZeroRateGains { get; init; }
        // Medicare IRMAA: this year's surcharge (from MAGI two years earlier, paid on top of spending), and this
        // year's MAGI - gross ordinary income plus all realized gains, harvested included - which sets the surcharge
        // two years from now.
        public required double IrmaaSurcharge { get; init; }
        public required double Magi { get; init; }
        public required bool AgeEligible { get; init; }
        public required double RothConversionAmount { get; init; }
        public required double RothConversionTax { get; init; }
        // The part of the conversion withheld for its tax (ordinary, and any capital gains tax it caused): money that
        // left Tax Deferred to pay tax rather than reaching Roth. 0 when Brokerage paid it all.
        public required double RothConversionTaxWithheld { get; init; }
        // Who paid the year's ordinary tax (reporting only; with SocialSecurityTax and AnnuityTax these sum to
        // OrdinaryTaxAmount): the tax the Tax Deferred withdrawal adds on top of Social Security and the annuity
        // (withheld from that withdrawal), and the conversion's ordinary tax split between the Brokerage sale and the
        // converted amount itself.
        public required double TaxDeferredWithdrawalTax { get; init; }
        public required double RothConversionOrdinaryTaxFromBrokerage { get; init; }
        public required double RothConversionOrdinaryTaxFromConversion { get; init; }
        public required double AgeInYear { get; init; }
        public required double TaxableWithdrawalPercentOfBalance { get; init; }
        public required double SocialSecurityIncome { get; init; }
        public required double SocialSecurityTax { get; init; }
        // Monthly Social Security payments received this year (fewer than 12 in the first year of benefits or a partial year)
        public required int SocialSecurityMonths { get; init; }
        // An annuity's payments this year (Annuity): the gross amount, the part that's ordinary income (the rest is a
        // tax-free return of basis), the tax that part adds on top of taxable Social Security (paid out of the
        // payments), and how many monthly payments fell in the year. All 0 without an annuity.
        public required double AnnuityIncome { get; init; }
        public required double AnnuityTaxableIncome { get; init; }
        public required double AnnuityTax { get; init; }
        public required int AnnuityPayments { get; init; }
        // The required minimum distribution taken from Tax Deferred this year (0 before RMDs start; part of
        // TaxableWithdrawal, its tax part of TaxDeferredWithdrawalTax), the Uniform Lifetime Table divisor behind it
        // (null before RMDs start), and its after-tax amount beyond the year's need, saved to Brokerage.
        public required double RequiredMinimumDistribution { get; init; }
        public required double? RmdDivisor { get; init; }
        public required double RmdSurplusToBrokerage { get; init; }
        // After-tax Social Security and annuity income beyond the year's spending, saved to Brokerage.
        public required double IncomeSurplusToBrokerage { get; init; }
    }
}
