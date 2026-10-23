namespace MonteCarloSimulation.Core
{
    // An income annuity the household already owns (its cost isn't taken from the balances): a fixed monthly payment
    // from FirstPayment, paid monthly on that date's day of the month for the rest of the horizon. Like Social Security
    // it's income in the year received, never deposited into an account. The payment is nominal - not scaled by the
    // model's inflation - unless AnnualIncrease (a COLA) raises it each January after the first payment's year.
    //
    // Taxes: a non-qualified annuity (bought with after-tax money) returns its Basis tax-free, Basis / 312 of each of
    // the first 312 payments (26 years; capped at the payment), and the rest of each payment is ordinary income; once
    // the basis is recovered the whole payment is ordinary income. A qualified one (inside a tax-deferred account) has
    // no basis: Basis 0, and every payment is ordinary income.
    public sealed record Annuity
    {
        // Payments over which the basis is recovered: 26 years of monthly payments.
        public const int BasisRecoveryPayments = 26 * 12;

        // The app's one COLA: 2% each January. A COLA annuity bought for the same money starts lower than a level one;
        // the app assumes its first year's payment is 90% of the level payment (the SPIA index quotes level payouts only).
        public const double ColaIncrease = 0.02;
        public const double ColaFirstYearFactor = 0.9;

        // The first payment's amount, in actual (nominal) dollars.
        public double MonthlyPayment { get; init; }
        public DateOnly FirstPayment { get; init; }
        // The after-tax money paid in, recovered tax-free; 0 for an annuity inside a tax-deferred account.
        public double Basis { get; init; }
        // The yearly cost-of-living increase (0.02 = 2%), applied each January after the first payment's year; 0 for none.
        public double AnnualIncrease { get; init; }

        // Each payment in `calendarYear` (every payment in a calendar year is the same).
        internal double PaymentIn(int calendarYear) =>
            MonthlyPayment * Math.Pow(1 + AnnualIncrease, Math.Max(0, calendarYear - FirstPayment.Year));

        // The tax-free part of each payment while the basis is being recovered.
        internal double ExcludedPerPayment(double payment) => Math.Min(Basis / BasisRecoveryPayments, payment);
    }
}
