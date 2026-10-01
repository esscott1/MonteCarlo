namespace MonteCarloSimulation.Core
{
    // Medicare's income-related monthly adjustment (IRMAA): once MAGI is above a threshold, Part B and Part D
    // premiums rise. Year Y's surcharge is set by MAGI from year Y - 2 and is paid for each month on Medicare that
    // year. Thresholds and surcharges are the 2026 single-filer table, scaled by the year's inflation factor like
    // the tax brackets.
    //
    // MAGI here is the year's gross ordinary income (taxable Social Security, Tax Deferred draws, conversions) plus
    // every realized gain, harvested gains included. The surcharge is a cost of the year on top of spending.
    internal static class MedicareIrmaa
    {
        // Lookback: the surcharge in a year is based on MAGI this many years earlier.
        public const int LookbackYears = 2;

        public static double MonthlySurcharge(double magi, double inflationFactor)
        {
            double surcharge = 0;
            var tiers = FederalTaxBrackets.MedicareIrmaaSingle2026;
            for (int i = 0; i < tiers.Count; i++)
            {
                var tier = tiers[i];
                if (magi <= tier.MagiAbove * inflationFactor) break;
                surcharge = tier.MonthlySurcharge * inflationFactor;
            }
            return surcharge;
        }

        // The year's surcharge, given MAGI from LookbackYears earlier. Null lookback MAGI - the first two years of
        // retirement, whose lookback is before the model starts - means no surcharge: SSA recalculates IRMAA after
        // a work stoppage (form SSA-44), so pre-retirement wages don't count.
        public static double YearSurcharge(double? lookbackMagi, RetirementYear year) =>
            lookbackMagi is double magi && year.MedicareMonths > 0
                ? MonthlySurcharge(magi, year.InflationFactor) * year.MedicareMonths
                : 0;
    }
}
