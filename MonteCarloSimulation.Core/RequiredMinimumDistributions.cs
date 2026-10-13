namespace MonteCarloSimulation.Core
{
    // Required minimum distributions (RMDs): from the calendar year a person reaches the start age, each year must draw
    // at least the Tax Deferred balance at the end of the previous year divided by the Uniform Lifetime Table's
    // distribution period for the age reached this year. Roth accounts have none.
    //
    // Start age: Internal Revenue Code section 401(a)(9)(C), as amended by SECURE 2.0 (2022) and confirmed by the
    // IRS's 2024 final regulations - 72 for those born in 1950 or earlier (already under way when the model starts),
    // 73 for 1951-1959, 75 for 1960 or later. A married household's spouse is assumed the same age, so the Uniform table
    // applies to both statuses (the Joint Life table is only for a spouse more than 10 years younger).
    internal static class RequiredMinimumDistributions
    {
        // The Uniform Lifetime Table, 26 CFR 1.401(a)(9)-9(c) (in force from 2022; IRS Publication 590-B, Appendix B,
        // Table III): the distribution period for each age from FirstTableAge, the last entry covering 120 and older.
        private const int FirstTableAge = 72;
        private static readonly double[] UniformLifetime =
        [
            27.4, 26.5, 25.5, 24.6, 23.7, 22.9, 22.0, 21.1, 20.2, 19.4, // 72-81
            18.5, 17.7, 16.8, 16.0, 15.2, 14.4, 13.7, 12.9, 12.2, 11.5, // 82-91
            10.8, 10.1, 9.5, 8.9, 8.4, 7.8, 7.3, 6.8, 6.4, 6.0,         // 92-101
            5.6, 5.2, 4.9, 4.6, 4.3, 4.1, 3.9, 3.7, 3.5, 3.4,           // 102-111
            3.3, 3.1, 3.0, 2.9, 2.8, 2.7, 2.5, 2.3, 2.0                 // 112-120+
        ];

        public static int StartAge(DateOnly birthdate) => birthdate.Year switch
        {
            <= 1950 => 72,
            <= 1959 => 73,
            _ => 75
        };

        // The distribution period for the age reached during the year.
        public static double Divisor(int age)
        {
            if (age < FirstTableAge) throw new ArgumentOutOfRangeException(nameof(age), age, $"The Uniform Lifetime Table starts at {FirstTableAge}.");
            return UniformLifetime[Math.Min(age - FirstTableAge, UniformLifetime.Length - 1)];
        }

        // The calendar year's divisor, or null before the year the start age is reached. The age is the one reached
        // on the birthday in that year.
        public static double? DivisorFor(DateOnly birthdate, int calendarYear)
        {
            int age = calendarYear - birthdate.Year;
            return age >= StartAge(birthdate) ? Divisor(age) : null;
        }
    }

    // One year's required minimum distribution. It comes out of Tax Deferred before anything else and is ordinary
    // income stacked on top of taxable Social Security (every other ordinary income then stacks on top of both).
    // Its after-tax amount covers the year's need first; anything beyond the need is Surplus, saved to Brokerage as
    // basis like surplus Social Security.
    internal readonly record struct RequiredDistributionYear(double Gross, double Tax, double Need, double Surplus)
    {
        // A year without an RMD: the need is unchanged.
        public static RequiredDistributionYear None(double need) => new(0, 0, need, 0);

        // `required` gross dollars drawn from Tax Deferred, taxed on top of `baseIncome`, against the year's `need`.
        public static RequiredDistributionYear Compute(double required, TaxYear taxYear, double baseIncome, double need)
        {
            if (required <= 0) return None(need);
            double tax = taxYear.IncrementalOrdinaryTax(baseIncome, required);
            double net = required - tax;
            return new RequiredDistributionYear(required, tax, Math.Max(0, need - net), Math.Max(0, net - need));
        }
    }
}
