namespace MonteCarloSimulation.Core
{
    // One simulated year's ordinary-income tax schedule: the (inflated) standard deduction plus the bracket
    // table scaled by the cumulative inflation factor. All ordinary-tax math - forward and inverse - lives here.
    internal readonly record struct TaxYear(double StandardDeduction, double InflationFactor, IReadOnlyList<TaxBracket> Brackets)
    {
        // Gross ordinary income at which the first bracket at 22% or higher starts - the ceiling that both the
        // tax-optimized Tax Deferred fill and Roth conversions fill up to. Read from the table, not hard-coded.
        public double Bracket22CeilingGross => StandardDeduction + Brackets.First(b => b.Rate >= 0.22).LowerBound * InflationFactor;

        // Forward tax calculation (the inverse of GrossUpOrdinary): ordinary tax owed on `grossIncome` after
        // exempting the standard deduction, marginally through the inflation-scaled brackets.
        public double OrdinaryTax(double grossIncome)
        {
            double taxableIncome = Math.Max(0, grossIncome - StandardDeduction);
            double tax = 0;

            foreach (var bracket in Brackets)
            {
                double lower = bracket.LowerBound * InflationFactor;
                double upper = bracket.UpperBound * InflationFactor;
                if (taxableIncome <= lower) break;
                tax += (Math.Min(taxableIncome, upper) - lower) * bracket.Rate;
            }

            return tax;
        }

        // Ordinary tax owed on `extraIncome` stacked on top of `baseGrossIncome` already counted this year.
        public double IncrementalOrdinaryTax(double baseGrossIncome, double extraIncome)
        {
            if (extraIncome <= 0) return 0;
            return OrdinaryTax(baseGrossIncome + extraIncome) - OrdinaryTax(baseGrossIncome);
        }

        // Computes the pre-tax ("gross") ordinary withdrawal that nets `desiredNet` dollars after tax, stacked on
        // top of `baseIncome` ordinary income already counted this year (the taxable share of Social Security).
        // Whatever standard deduction the base didn't use is tax-free; the rest is taxed marginally from where
        // the base left off in the inflation-scaled brackets. Each bracket is linear, so this is an exact
        // analytic inversion — no iteration required. baseIncome = 0 is the plain case.
        public double GrossUpOrdinary(double desiredNet, double baseIncome)
        {
            double deductionRoom = Math.Max(0, StandardDeduction - baseIncome);
            if (desiredNet <= deductionRoom) return desiredNet;

            double remainingNet = desiredNet - deductionRoom;
            double position = Math.Max(0, baseIncome - StandardDeduction);
            double grossAboveDeduction = 0;

            foreach (var bracket in Brackets)
            {
                double lower = bracket.LowerBound * InflationFactor;
                double upper = bracket.UpperBound * InflationFactor;
                if (position >= upper) continue;

                double bracketWidth = upper - Math.Max(lower, position);
                double bracketNetCapacity = bracketWidth * (1 - bracket.Rate);

                if (remainingNet <= bracketNetCapacity || double.IsPositiveInfinity(upper))
                {
                    grossAboveDeduction += remainingNet / (1 - bracket.Rate);
                    remainingNet = 0;
                    break;
                }

                grossAboveDeduction += bracketWidth;
                remainingNet -= bracketNetCapacity;
            }

            return deductionRoom + grossAboveDeduction;
        }

        // Finds the marginal ordinary-income bracket `grossOrdinaryIncome` falls into, and how many more gross
        // dollars could be added before crossing into the next bracket up. Mirrors GrossUpOrdinary's accounting:
        // the amount above the standard deduction maps 1:1 onto the (inflation-scaled) bracket thresholds, so no
        // re-inversion is needed here. AmountUntilNextBracket/NextBracketRate are null once already in the top
        // bracket, since there's no next bracket to reach; CurrentBracketRate is always set (0 if income never
        // exceeds the standard deduction, matching the first bracket's floor).
        public (double CurrentBracketRate, double? AmountUntilNextBracket, double? NextBracketRate) BracketRoom(double grossOrdinaryIncome)
        {
            double grossAboveDeduction = Math.Max(0, grossOrdinaryIncome - StandardDeduction);

            for (int i = 0; i < Brackets.Count; i++)
            {
                double upper = Brackets[i].UpperBound * InflationFactor;
                if (double.IsPositiveInfinity(upper)) return (Brackets[i].Rate, null, null);

                // Income sitting exactly at a threshold (e.g. a conversion that fills to it) belongs to the
                // lower bracket - no dollar was taxed at the next rate. Tolerance absorbs float rounding.
                if (grossAboveDeduction <= upper + 1e-6)
                {
                    double? nextRate = i + 1 < Brackets.Count ? Brackets[i + 1].Rate : (double?)null;
                    return (Brackets[i].Rate, Math.Max(0, upper - grossAboveDeduction), nextRate);
                }
            }

            return (Brackets[^1].Rate, null, null);
        }
    }
}
