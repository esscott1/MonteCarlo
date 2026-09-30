namespace MonteCarloSimulation.Core
{
    // The conversion ceiling for each RothConversionTarget: the gross ordinary income a year's conversion fills up
    // to. Bracket lines are read from the (inflation-scaled) table, not hard-coded.
    internal static class ConversionTargets
    {
        public static readonly ConversionCeiling TopOf22 = (taxYear, _, _) => BracketStart(taxYear, 0.24);
        public static readonly ConversionCeiling TopOf24 = (taxYear, _, _) => BracketStart(taxYear, 0.32);

        // Null for None: that target converts nothing.
        public static ConversionCeiling? CeilingFor(RothConversionTarget target) => target switch
        {
            RothConversionTarget.None => null,
            RothConversionTarget.Bracket12 => RothConversion.DefaultCeiling,
            RothConversionTarget.Bracket22 => TopOf22,
            RothConversionTarget.Bracket24 => TopOf24,
            _ => throw new InvalidOperationException("Resolve RothConversionTarget.Automatic (AutomaticStrategy.Resolve) before running.")
        };

        // Gross ordinary income where the first bracket at `rate` or higher starts.
        public static double BracketStart(TaxYear taxYear, double rate) =>
            taxYear.StandardDeduction + taxYear.Brackets.First(b => b.Rate >= rate).LowerBound * taxYear.InflationFactor;
    }
}
