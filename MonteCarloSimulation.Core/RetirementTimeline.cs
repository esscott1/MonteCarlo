namespace MonteCarloSimulation.Core
{
    // One model year: the part of a calendar year that falls inside the retirement window. The first year starts
    // on the retirement date and the last ends on its anniversary, so both are partial unless retirement starts
    // on January 1. Every dollar amount for the year is derived from here. RmdDivisor is the year's required minimum
    // distribution divisor (RequiredMinimumDistributions), null before RMDs start. The annuity's payments, gross
    // income and taxable part (Annuity) don't depend on markets either, so they're laid out here too (0 without one).
    internal sealed record RetirementYear(
        int Index,
        int CalendarYear,
        DateOnly Start,
        DateOnly End,
        double Fraction,
        double InflationFactor,
        double AgeAtStart,
        int SocialSecurityPayments,
        int AnnuityPayments,
        double AnnuityIncome,
        double AnnuityTaxable,
        int MedicareMonths,
        int MedicarePeople,
        double? RmdDivisor,
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
            var tables = FederalTaxBrackets.For(parameters.FilingStatus);

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
                var (annuityPayments, annuityIncome, annuityTaxable) = AnnuityYearIncome(parameters.Annuity, calendarYear, start, stop);
                int medicareMonths = MedicareMonths(parameters.Birthdate, start, stop);
                double? rmdDivisor = RequiredMinimumDistributions.DivisorFor(parameters.Birthdate, calendarYear);
                var taxYear = TaxYear.For(tables, parameters.AnnualStandardDeduction * inflationFactor, inflationFactor);

                years.Add(new RetirementYear(years.Count, calendarYear, start, stop, fraction, inflationFactor, ageAtStart, payments,
                    annuityPayments, annuityIncome, annuityTaxable, medicareMonths, tables.MedicarePeople, rmdDivisor, taxYear));
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

        // Months of Medicare coverage in [from, to): coverage starts on the first day of the 65th-birthday month. A
        // married household's spouse is assumed the same age, so both start together (RetirementYear.MedicarePeople).
        // Counted like monthly payments due on the 1st.
        public static int MedicareMonths(DateOnly birthdate, DateOnly from, DateOnly to)
        {
            var sixtyFive = birthdate.AddYears(65);
            return SocialSecurityPayments(new DateOnly(sixtyFive.Year, sixtyFive.Month, 1), from, to);
        }

        // The annuity's payments falling in [from, to) (a calendar year's part of the window), their gross amount, and
        // the part that's ordinary income: all of it, less Basis / 312 of each payment among the first 312 - counted
        // from the first payment, even one before the window starts.
        public static (int Payments, double Income, double Taxable) AnnuityYearIncome(Annuity? annuity, int calendarYear, DateOnly from, DateOnly to)
        {
            if (annuity is null) return (0, 0, 0);
            int payments = SocialSecurityPayments(annuity.FirstPayment, from, to);
            if (payments == 0) return (0, 0, 0);
            double payment = annuity.PaymentIn(calendarYear);
            int paidBefore = SocialSecurityPayments(annuity.FirstPayment, annuity.FirstPayment, from);
            int recovering = Math.Min(payments, Math.Max(0, Annuity.BasisRecoveryPayments - paidBefore));
            double income = payment * payments;
            return (payments, income, income - recovering * annuity.ExcludedPerPayment(payment));
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
