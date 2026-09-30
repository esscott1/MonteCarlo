namespace MonteCarloSimulation.Core
{
    // The gross ordinary income a year's Roth conversion fills up to, given the ordinary income and realized gains
    // already counted that year. The app always uses RothConversion.DefaultCeiling; the Strategy Lab plugs in
    // alternatives to compare against it.
    internal delegate double ConversionCeiling(TaxYear taxYear, double ordinaryIncomeSoFar, double gainsSoFar);

    // Where a conversion's tax may come from this year. AgeEligible is the year's 59.5 gate; BridgeNeed is the
    // spending still to come before the gate opens (today's model years after this one), which BridgeAware keeps
    // in Brokerage.
    internal readonly record struct ConversionFunding(ConversionTaxFunding Rule, bool AgeEligible, double BridgeNeed)
    {
        public static readonly ConversionFunding BrokerageOnly = new(ConversionTaxFunding.Brokerage, true, 0);
    }

    // Converts Tax Deferred money to Roth after the year's regular withdrawal, filling the remaining cheap bracket
    // room up to TaxYear.OrdinaryFillCeiling (the start of the 22% bracket, or lower if the conversion would push
    // this year's 0%-band gains into 15%); years already past it convert nothing. A conversion isn't a
    // withdrawal, so the 59.5 gate doesn't apply.
    //
    // The conversion's cost is its own ordinary Tax plus the extra capital gains tax it causes by stacking beneath
    // the gains already realized this year. It's paid by selling Brokerage (TaxSale, whose own gains are taxed
    // too) and/or out of the converted amount itself (Withheld: that much of Amount never reaches Roth), per the
    // ConversionTaxFunding rule. Paying from the conversion is the same, dollar for dollar, as withholding from
    // Tax Deferred or paying from Roth afterwards, so those aren't separate options.
    //
    // OrdinaryTaxFromConversion is the part of the ordinary Tax paid out of the conversion (in proportion to the
    // withheld share of the cost); the rest was paid by the Brokerage sale.
    internal readonly record struct RothConversion(
        double Amount, double Tax, double TaxSale, double RealizedGains, double Withheld, double OrdinaryTaxFromConversion)
    {
        public static readonly RothConversion None = default;

        // Capital gains tax attributable to the conversion: the bump on this year's earlier gains plus the
        // gains tax on the sale that funded it.
        public double CapitalGainsTax => TaxSale + Withheld - Tax;

        public double OrdinaryTaxFromBrokerage => Tax - OrdinaryTaxFromConversion;

        public static readonly ConversionCeiling DefaultCeiling = (taxYear, ordinaryIncome, gains) => taxYear.OrdinaryFillCeiling(ordinaryIncome, gains);

        public static RothConversion Apply(Accounts accounts, TaxYear taxYear, double ordinaryIncomeSoFar, double gainsSoFar) =>
            Apply(accounts, taxYear, ordinaryIncomeSoFar, gainsSoFar, DefaultCeiling, ConversionFunding.BrokerageOnly);

        public static RothConversion Apply(
            Accounts accounts, TaxYear taxYear, double ordinaryIncomeSoFar, double gainsSoFar, ConversionCeiling conversionCeiling, ConversionFunding funding)
        {
            double ceiling = conversionCeiling(taxYear, ordinaryIncomeSoFar, gainsSoFar);
            double fullAmount = Math.Min(Math.Max(0, ceiling - ordinaryIncomeSoFar), Math.Max(0, accounts.Taxable));
            if (fullAmount <= 0) return None;

            double gainFraction = accounts.BrokerageGainFraction;
            double brokerage = Math.Max(0, accounts.Brokerage);

            (double Tax, double Due) TaxOn(double amount)
            {
                double ordinaryTax = taxYear.IncrementalOrdinaryTax(ordinaryIncomeSoFar, amount);
                double gainsBump = taxYear.CapitalGainsTax(ordinaryIncomeSoFar + amount, gainsSoFar)
                    - taxYear.CapitalGainsTax(ordinaryIncomeSoFar, gainsSoFar);
                return (ordinaryTax, ordinaryTax + gainsBump);
            }
            double SaleFor(double net, double amount) =>
                taxYear.GrossUpBrokerageSale(net, gainFraction, ordinaryIncomeSoFar + amount, gainsSoFar);

            if (funding.Rule == ConversionTaxFunding.Brokerage)
            {
                // Brokerage pays everything. If it can't fund the full conversion, convert the most it can fund. The
                // required sale grows with the amount converted, so the search is monotone.
                double converted = fullAmount;
                double sale = SaleFor(TaxOn(fullAmount).Due, fullAmount);
                if (sale > brokerage)
                {
                    converted = Bisection.Largest(0, fullAmount, amount => SaleFor(TaxOn(amount).Due, amount) <= brokerage);
                    sale = SaleFor(TaxOn(converted).Due, converted);
                }

                // Clamp: the sale is at most the balance, so float rounding can't leave Brokerage at -1e-10
                double taxSale = Math.Min(sale, brokerage);
                accounts.SellBrokerage(taxSale);
                accounts.ConvertToRoth(converted, withheld: 0);
                return new RothConversion(converted, TaxOn(converted).Tax, taxSale, taxSale * gainFraction, 0, 0);
            }

            // The other rules never shrink the conversion: whatever Brokerage may pay, it pays first, and the rest
            // comes out of the conversion.
            var (tax, due) = TaxOn(fullAmount);
            double brokerageForTax = funding.Rule switch
            {
                ConversionTaxFunding.FromConversion => 0,
                // Keep the spending still to come before 59.5 in Brokerage, the only other money reachable then
                ConversionTaxFunding.BridgeAware when !funding.AgeEligible => Math.Max(0, brokerage - funding.BridgeNeed),
                _ => brokerage
            };
            double netFromBrokerage = Math.Min(due, taxYear.BrokerageNetCapacity(brokerageForTax, gainFraction, ordinaryIncomeSoFar + fullAmount, gainsSoFar));
            double brokerageSale = netFromBrokerage > 0 ? Math.Min(brokerageForTax, SaleFor(netFromBrokerage, fullAmount)) : 0;
            double withheld = Math.Max(0, due - netFromBrokerage);

            accounts.SellBrokerage(brokerageSale);
            accounts.ConvertToRoth(fullAmount, withheld);
            double ordinaryFromConversion = due > 0 ? tax * withheld / due : 0;
            return new RothConversion(fullAmount, tax, brokerageSale, brokerageSale * gainFraction, withheld, ordinaryFromConversion);
        }
    }
}
