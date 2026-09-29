namespace MonteCarloSimulation.Core
{
    // Converts Tax Deferred money to Roth after the year's regular withdrawal, filling the remaining 10%/12%
    // bracket room up to the start of the 22% bracket; years already at 22%+ convert nothing. The conversion's
    // tax is paid by selling Brokerage (TaxSale), which incurs its own LTCG. A conversion isn't a withdrawal,
    // so the 59.5 gate doesn't apply.
    internal readonly record struct RothConversion(double Amount, double Tax, double TaxSale)
    {
        public static readonly RothConversion None = default;

        // LTCG on the Brokerage sale that funded the conversion's tax.
        public double CapitalGainsTax => TaxSale - Tax;

        public static RothConversion Apply(Accounts accounts, TaxYear taxYear, double ordinaryIncomeSoFar)
        {
            double amount = Math.Min(Math.Max(0, taxYear.Bracket22CeilingGross - ordinaryIncomeSoFar), Math.Max(0, accounts.Taxable));
            double tax = taxYear.IncrementalOrdinaryTax(ordinaryIncomeSoFar, amount);

            double gainFraction = accounts.BrokerageGainFraction;
            double maxTaxPayable = TaxAssumptions.BrokerageNetCapacity(accounts.Brokerage, gainFraction);
            if (tax > maxTaxPayable)
            {
                // Tax on extra income is convex with f(0)=0, so f(k*x) <= k*f(x): scaling the
                // conversion by k guarantees the resulting tax fits within maxTaxPayable.
                amount *= maxTaxPayable / tax;
                tax = taxYear.IncrementalOrdinaryTax(ordinaryIncomeSoFar, amount);
            }

            double taxSale = TaxAssumptions.GrossUpLtcg(tax, gainFraction);
            accounts.SellBrokerage(taxSale);
            accounts.ConvertToRoth(amount);

            return new RothConversion(amount, tax, taxSale);
        }
    }
}
