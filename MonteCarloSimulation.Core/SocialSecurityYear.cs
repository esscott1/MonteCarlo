namespace MonteCarloSimulation.Core
{
    // Social Security is income in the year received, never deposited into an account. Its taxable share is
    // ordinary income stacked first in the brackets (so everything else - Tax Deferred draws, conversions -
    // stacks on top of it), and its after-tax amount reduces what the portfolio must supply this year. Any
    // after-tax excess over the year's spending is Surplus, saved to Brokerage as basis.
    internal readonly record struct SocialSecurityYear(double Gross, double Taxable, double Tax, double Need, double Surplus)
    {
        public static SocialSecurityYear Compute(double benefit, TaxYear taxYear, double spending)
        {
            double taxable = TaxAssumptions.SsTaxableFraction * benefit;
            double tax = taxYear.OrdinaryTax(taxable);
            double net = benefit - tax;
            return new SocialSecurityYear(benefit, taxable, tax, Math.Max(0, spending - net), Math.Max(0, net - spending));
        }
    }
}
