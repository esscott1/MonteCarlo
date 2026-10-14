namespace MonteCarloSimulation.Core
{
    // One year's annuity income (Annuity). Like Social Security it's never deposited into an account: its taxable part
    // is ordinary income stacked on top of taxable Social Security (every other ordinary income - the RMD, Tax Deferred
    // draws, conversions - then stacks on top of both), and its after-tax amount reduces what the portfolio must supply.
    // Anything beyond the year's need is Surplus, saved to Brokerage as basis like surplus Social Security.
    internal readonly record struct AnnuityYear(double Gross, double Taxable, double Tax, double Need, double Surplus)
    {
        // A year without annuity payments: the need is unchanged.
        public static AnnuityYear None(double need) => new(0, 0, 0, need, 0);

        // `gross` dollars received, `taxable` of them ordinary income taxed on top of `baseIncome`, against the year's `need`.
        public static AnnuityYear Compute(double gross, double taxable, TaxYear taxYear, double baseIncome, double need)
        {
            if (gross <= 0) return None(need);
            double tax = taxYear.IncrementalOrdinaryTax(baseIncome, taxable);
            double net = gross - tax;
            return new AnnuityYear(gross, taxable, tax, Math.Max(0, need - net), Math.Max(0, net - need));
        }
    }
}
