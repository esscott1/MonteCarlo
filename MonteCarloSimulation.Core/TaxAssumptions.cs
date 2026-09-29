namespace MonteCarloSimulation.Core
{
    // Flat-rate tax assumptions that don't depend on the year's bracket schedule.
    internal static class TaxAssumptions
    {
        // Taxable share of Social Security - a conservative simplification of the IRS provisional-income
        // formula (which taxes 0%, 50% or up to 85% depending on other income).
        public const double SsTaxableFraction = 0.85;
    }
}
