namespace MonteCarloSimulation.Web
{
    public enum AnnuitantSex { Male, Female }

    // The LifeAnnuities.us SPIA Payout Index's five figures: best monthly income per $100,000 of premium, single life (or
    // joint, 100% to the survivor) with a 5-year period certain, first payment one month after purchase.
    public sealed record SpiaCells(double Male65, double Male70, double Male75, double Female65, double Joint65)
    {
        public bool Valid => new[] { Male65, Male70, Male75, Female65, Joint65 }.All(v => double.IsFinite(v) && v > 0);
    }

    // A SPIA payout for any age from 55 to 80, estimated from the index's few figures with a standard annuity-pricing
    // formula: monthly payments, the first 60 certain and the rest while the annuitant lives (Gompertz mortality, modal
    // age m and a fixed dispersion b of 7 years), discounted at one interest rate i. The men's 65, 70 and 75 figures fix
    // i and m (within about 1%); the women's 65 figure fixes their modal age. The dispersion was chosen so the estimates
    // track the site's published best-by-age table (October 2026): every age 55 to 80, men and women, within about 3.5%.
    // So the page calls it an estimate. Pure math, no I/O.
    public sealed class SpiaPayoutModel
    {
        public const int MinAge = 55;
        public const int MaxAge = 80;
        private const int CertainMonths = 60;
        private const int Months = 12 * 60;
        private const double FixedDispersion = 7;

        private readonly SpiaCells _cells;

        private SpiaPayoutModel(SpiaCells cells, double interest, double maleModal, double dispersion, double femaleModal)
        {
            _cells = cells;
            Interest = interest;
            MaleModal = maleModal;
            Dispersion = dispersion;
            FemaleModal = femaleModal;
        }

        public double Interest { get; }
        public double MaleModal { get; }
        public double Dispersion { get; }
        public double FemaleModal { get; }

        public static SpiaPayoutModel Fit(SpiaCells cells)
        {
            // The men's three figures fix the interest rate and modal age, by squared relative error: a coarse grid, then
            // finer ones around the best point
            var men = new[] { (65.0, cells.Male65), (70.0, cells.Male70), (75.0, cells.Male75) };
            double Error(double i, double m) =>
                men.Sum(cell => Math.Pow(Payout(cell.Item1, i, m, FixedDispersion) / cell.Item2 - 1, 2));

            double bestI = 0.05, bestM = 90, bestError = double.MaxValue;
            double stepI = 0.0025, stepM = 1;
            (double lo, double hi) rangeI = (0.005, 0.15), rangeM = (75, 110);
            for (int round = 0; round < 6; round++)
            {
                for (double i = rangeI.lo; i <= rangeI.hi + 1e-12; i += stepI)
                    for (double m = rangeM.lo; m <= rangeM.hi + 1e-12; m += stepM)
                    {
                        double error = Error(i, m);
                        if (error < bestError) (bestError, bestI, bestM) = (error, i, m);
                    }
                rangeI = (Math.Max(0.0001, bestI - 2 * stepI), bestI + 2 * stepI);
                rangeM = (bestM - 2 * stepM, bestM + 2 * stepM);
                stepI /= 5;
                stepM /= 5;
            }
            double bestB = FixedDispersion;

            // The women's modal age: a later one pays less, so bisect for the one that matches their 65 figure
            double lo = 60, hi = 130;
            for (int k = 0; k < 60; k++)
            {
                double mid = (lo + hi) / 2;
                if (Payout(65, bestI, mid, bestB) > cells.Female65) lo = mid; else hi = mid;
            }
            return new SpiaPayoutModel(cells, bestI, bestM, bestB, (lo + hi) / 2);
        }

        // Monthly income per $100,000 at an age nearest birthday (how carriers price), or null outside 55 to 80. The
        // index's own figure where it has one for that age and sex, else the formula's estimate.
        public (double Monthly, bool Estimated)? MonthlyPer100k(double age, AnnuitantSex sex)
        {
            int nearest = (int)Math.Round(age, MidpointRounding.AwayFromZero);
            if (nearest < MinAge || nearest > MaxAge) return null;

            double? published = (sex, nearest) switch
            {
                (AnnuitantSex.Male, 65) => _cells.Male65,
                (AnnuitantSex.Male, 70) => _cells.Male70,
                (AnnuitantSex.Male, 75) => _cells.Male75,
                (AnnuitantSex.Female, 65) => _cells.Female65,
                _ => null
            };
            if (published is double figure) return (figure, false);
            return (Estimate(nearest, sex), true);
        }

        // The formula's payout at an age, even where the index has its own figure
        public double Estimate(double age, AnnuitantSex sex) =>
            Payout(age, Interest, sex == AnnuitantSex.Male ? MaleModal : FemaleModal, Dispersion);

        // 100,000 over the present value of $1 a month: the first payment a month after purchase, 60 certain, then while
        // the annuitant lives, out to 60 years
        internal static double Payout(double age, double interest, double modal, double dispersion)
        {
            double monthlyDiscount = Math.Pow(1 + interest, -1.0 / 12);
            double hazard = Math.Exp((age - modal) / dispersion);
            double monthlyGrowth = Math.Exp(1 / (12 * dispersion));
            double value = 0, discount = 1, growth = 1; // growth = e^(t / b) at t = k / 12 years
            for (int k = 1; k <= Months; k++)
            {
                discount *= monthlyDiscount;
                growth *= monthlyGrowth;
                double alive = k <= CertainMonths ? 1 : Math.Exp(-hazard * (growth - 1));
                value += discount * alive;
            }
            return 100_000 / value;
        }
    }
}
