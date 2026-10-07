namespace MonteCarloSimulation.Core
{
    // Medicare's income-related monthly adjustment (IRMAA): once MAGI is above a threshold, Part B and Part D
    // premiums rise. Year Y's surcharge is set by MAGI from year Y - 2 and is paid for each month on Medicare that
    // year by each person on Medicare (a married couple: both, the spouse assumed the same age). Thresholds and
    // surcharges are the filing status's 2026 table (the year's TaxYear.IrmaaTiers), scaled by the year's inflation
    // factor like the tax brackets.
    //
    // MAGI here is the year's gross ordinary income (taxable Social Security, Tax Deferred draws, conversions) plus
    // every realized gain, harvested gains included. The surcharge is a cost of the year on top of spending.
    internal static class MedicareIrmaa
    {
        // Lookback: the surcharge in a year is based on MAGI this many years earlier.
        public const int LookbackYears = 2;

        // One person's monthly surcharge.
        public static double MonthlySurcharge(double magi, IReadOnlyList<IrmaaTier> tiers, double inflationFactor)
        {
            double surcharge = 0;
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
                ? MonthlySurcharge(magi, year.TaxYear.IrmaaTiers, year.InflationFactor) * year.MedicareMonths * year.MedicarePeople
                : 0;
    }
}
