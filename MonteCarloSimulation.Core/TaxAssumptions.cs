namespace MonteCarloSimulation.Core
{
    // Flat-rate tax assumptions that don't depend on the year's bracket schedule.
    internal static class TaxAssumptions
    {
        public const double LtcgRate = 0.20;

        // Taxable share of Social Security - a conservative simplification of the IRS provisional-income
        // formula (which taxes 0%, 50% or up to 85% depending on other income).
        public const double SsTaxableFraction = 0.85;

        // Computes the pre-tax ("gross") Brokerage withdrawal that nets `desiredNet` dollars after tax,
        // taxing only the `gainFraction` portion of each withdrawn dollar at the flat LTCG rate - the
        // remaining (basis) portion is a tax-free return of principal. Unlike the ordinary gross-up,
        // this is flat-rate: no brackets, no standard-deduction exemption.
        public static double GrossUpLtcg(double desiredNet, double gainFraction)
        {
            if (desiredNet <= 0) return 0;
            double taxableFraction = Math.Clamp(gainFraction, 0.0, 1.0) * LtcgRate;
            return desiredNet / (1 - taxableFraction);
        }

        // What a Brokerage balance can net after tax if sold entirely.
        public static double BrokerageNetCapacity(double balance, double gainFraction) =>
            Math.Max(0, balance) * (1 - gainFraction * LtcgRate);
    }
}
