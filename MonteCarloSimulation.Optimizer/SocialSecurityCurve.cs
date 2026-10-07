namespace MonteCarloSimulation.Optimizer
{
    // Monthly Social Security benefit for any claiming age, built from three estimates (ages 62, 67 and 70, in
    // today's dollars, as on an SSA statement). Between them it follows the shape of SSA's own adjustments, so
    // it passes exactly through all three:
    //  - Before full retirement age (67), SSA reduces the benefit 5/9% per month for the first 36 months early
    //    and 5/12% per month beyond that - 30% in all at 62. The same shape is scaled to the entered 62 amount.
    //  - After 67, delayed retirement credits accrue linearly (2/3% per month) to 70.
    public sealed record SocialSecurityCurve(double At62, double At67, double At70)
    {
        public const int EarliestAge = 62;
        public const int FullRetirementAge = 67;
        public const int LatestAge = 70;

        private const double FullEarlyReductionPercent = 30.0;

        // SSA's own adjustments for someone whose full retirement age is 67: 30% less at 62, and 8% a year in delayed
        // credits to 70 (24% more)
        public const double SsaShareAt62 = 1 - FullEarlyReductionPercent / 100;
        public const double SsaShareAt70 = 1.24;

        // The curve from one amount, the benefit at full retirement age (as on an SSA statement), with the 62 and 70
        // amounts set by SSA's rules rather than entered
        public static SocialSecurityCurve FromFullRetirementAmount(double at67) =>
            new(at67 * SsaShareAt62, at67, at67 * SsaShareAt70);

        public double MonthlyBenefitAtAge(int age) => MonthlyBenefitAtMonths(age * 12);

        public double MonthlyBenefitAtMonths(int claimAgeMonths)
        {
            int fullRetirementMonths = FullRetirementAge * 12;
            if (claimAgeMonths <= fullRetirementMonths)
            {
                int monthsEarly = Math.Clamp(fullRetirementMonths - claimAgeMonths, 0, (FullRetirementAge - EarliestAge) * 12);
                double reductionPercent = 5.0 / 9 * Math.Min(monthsEarly, 36) + 5.0 / 12 * Math.Max(0, monthsEarly - 36);
                return At67 - (At67 - At62) * reductionPercent / FullEarlyReductionPercent;
            }

            int monthsDelayed = Math.Clamp(claimAgeMonths - fullRetirementMonths, 0, (LatestAge - FullRetirementAge) * 12);
            return At67 + (At70 - At67) * monthsDelayed / ((LatestAge - FullRetirementAge) * 12.0);
        }
    }
}
