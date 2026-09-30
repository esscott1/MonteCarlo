namespace MonteCarloSimulation.Core
{
    // One model year: the part of a calendar year that falls inside the retirement window. The first year starts
    // on the retirement date and the last ends on its anniversary, so both are partial unless retirement starts
    // on January 1. Every dollar amount for the year is derived from here.
    internal sealed record RetirementYear(
        int Index,
        int CalendarYear,
        DateOnly Start,
        DateOnly End,
        double Fraction,
        double InflationFactor,
        double AgeAtStart,
        int SocialSecurityPayments,
        TaxYear TaxYear)
    {
        // The 59.5 early-withdrawal gate, judged at the start of the year: turning 59.5 mid-year unlocks
        // Tax Deferred and Roth gains from the following year.
        public bool AgeEligible => AgeAtStart >= 59.5;
    }

    // Lays the retirement window - RetirementDate to Years later - onto calendar (tax) years. Deterministic: the
    // same parameters always give the same timeline, independent of today's date.
    internal static class RetirementTimeline
    {
        // The year of the bracket tables, and the "today's dollars" year for spending, Social Security and the
        // standard deduction. Inflation is compounded from here.
        public const int BaseYear = FederalTaxBrackets.Year;

        // 2.5% a year through the first 20 years of retirement (and any years before it), 1% after.
        private const double EarlyInflation = 0.025;
        private const double LateInflation = 0.01;
        private const int EarlyInflationRetirementYears = 20;

        public static IReadOnlyList<RetirementYear> Build(SimulationParameters parameters)
        {
            var retirement = parameters.RetirementDate;
            var end = retirement.AddYears(parameters.Years);
            var years = new List<RetirementYear>();

            for (int calendarYear = retirement.Year; ; calendarYear++)
            {
                var yearStart = new DateOnly(calendarYear, 1, 1);
                var nextYearStart = yearStart.AddYears(1);
                var start = retirement > yearStart ? retirement : yearStart;
                var stop = end < nextYearStart ? end : nextYearStart;
                if (start >= stop) break;

                double fraction = (double)(stop.DayNumber - start.DayNumber) / (nextYearStart.DayNumber - yearStart.DayNumber);
                double inflationFactor = InflationFactor(calendarYear, retirement.Year);
                double ageAtStart = (start.DayNumber - parameters.Birthdate.DayNumber) / 365.25;
                int payments = SocialSecurityPayments(parameters.SocialSecurityStartDate, start, stop);
                var taxYear = new TaxYear(
                    parameters.AnnualStandardDeduction * inflationFactor, inflationFactor,
                    FederalTaxBrackets.Single2026, FederalTaxBrackets.CapitalGainsSingle2026);

                years.Add(new RetirementYear(years.Count, calendarYear, start, stop, fraction, inflationFactor, ageAtStart, payments, taxYear));
            }

            return years;
        }

        // Cumulative inflation from BaseYear to `calendarYear`.
        public static double InflationFactor(int calendarYear, int retirementYear)
        {
            double factor = 1.0;
            for (int year = BaseYear; year < calendarYear; year++)
                factor *= 1 + (year < retirementYear + EarlyInflationRetirementYears ? EarlyInflation : LateInflation);
            return factor;
        }

        // Social Security pays monthly on the start date's day of the month (clamped to shorter months), beginning
        // on the start date. Counts the payments falling in [from, to).
        public static int SocialSecurityPayments(DateOnly firstPayment, DateOnly from, DateOnly to)
        {
            if (firstPayment >= to) return 0;

            // Start a month early to be safe about month-length clamping, then count exactly.
            int n = Math.Max(0, (from.Year - firstPayment.Year) * 12 + from.Month - firstPayment.Month - 1);
            int count = 0;
            for (var payment = firstPayment.AddMonths(n); payment < to; payment = firstPayment.AddMonths(++n))
            {
                if (payment >= from) count++;
            }
            return count;
        }
    }
}
