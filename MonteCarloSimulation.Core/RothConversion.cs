namespace MonteCarloSimulation.Core
{
    // Converts Tax Deferred money to Roth after the year's regular withdrawal, filling the remaining cheap bracket
    // room up to TaxYear.OrdinaryFillCeiling (the start of the 22% bracket, or lower if the conversion would push
    // this year's 0%-band gains into 15%); years already past it convert nothing. A conversion isn't a
    // withdrawal, so the 59.5 gate doesn't apply.
    //
    // The conversion's full cost is paid by selling Brokerage (TaxSale): its own ordinary Tax, plus the extra
    // capital gains tax it causes by stacking beneath the gains already realized this year, plus the capital
    // gains tax on the funding sale itself.
    internal readonly record struct RothConversion(double Amount, double Tax, double TaxSale, double RealizedGains)
    {
        public static readonly RothConversion None = default;

        // Capital gains tax attributable to the conversion: the bump on this year's earlier gains plus the
        // gains tax on the sale that funded it.
        public double CapitalGainsTax => TaxSale - Tax;

        public static RothConversion Apply(Accounts accounts, TaxYear taxYear, double ordinaryIncomeSoFar, double gainsSoFar)
        {
            double ceiling = taxYear.OrdinaryFillCeiling(ordinaryIncomeSoFar, gainsSoFar);
            double fullAmount = Math.Min(Math.Max(0, ceiling - ordinaryIncomeSoFar), Math.Max(0, accounts.Taxable));
            if (fullAmount <= 0) return None;

            double gainFraction = accounts.BrokerageGainFraction;
            double brokerage = Math.Max(0, accounts.Brokerage);

            (double Tax, double Sale) Cost(double amount)
            {
                double ordinaryTax = taxYear.IncrementalOrdinaryTax(ordinaryIncomeSoFar, amount);
                double gainsBump = taxYear.CapitalGainsTax(ordinaryIncomeSoFar + amount, gainsSoFar)
                    - taxYear.CapitalGainsTax(ordinaryIncomeSoFar, gainsSoFar);
                double sale = taxYear.GrossUpBrokerageSale(ordinaryTax + gainsBump, gainFraction, ordinaryIncomeSoFar + amount, gainsSoFar);
                return (ordinaryTax, sale);
            }

            // If Brokerage can't fund the full conversion, convert the most it can fund. The required sale grows
            // with the amount converted, so the search is monotone.
            double converted = fullAmount;
            var cost = Cost(fullAmount);
            if (cost.Sale > brokerage)
            {
                converted = Bisection.Largest(0, fullAmount, amount => Cost(amount).Sale <= brokerage);
                cost = Cost(converted);
            }

            // Clamp: the sale is at most the balance, so float rounding can't leave Brokerage at -1e-10
            double taxSale = Math.Min(cost.Sale, brokerage);
            accounts.SellBrokerage(taxSale);
            accounts.ConvertToRoth(converted);

            return new RothConversion(converted, cost.Tax, taxSale, taxSale * gainFraction);
        }
    }
}
