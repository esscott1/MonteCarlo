namespace MonteCarloSimulation.Core
{
    // One simulated year's federal tax schedule: the (inflated) standard deduction plus the ordinary and long-term
    // capital gains bracket tables, both scaled by the cumulative inflation factor. All tax math - forward and
    // inverse, ordinary and capital gains - lives here.
    //
    // Income is measured gross (before the deduction). Ordinary income fills the deduction and brackets first;
    // long-term gains stack on top of it, so any deduction ordinary income didn't use shelters gains, and gains
    // are taxed at 0/15/20% according to where they sit in total taxable income.
    internal readonly record struct TaxYear(
        double StandardDeduction,
        double InflationFactor,
        IReadOnlyList<TaxBracket> Brackets,
        IReadOnlyList<TaxBracket> GainsBrackets)
    {
        // Gross ordinary income at which the first bracket at 22% or higher starts - the ceiling that both the
        // tax-optimized Tax Deferred fill and Roth conversions fill up to. Read from the table, not hard-coded.
        public double Bracket22CeilingGross => StandardDeduction + Brackets.First(b => b.Rate >= 0.22).LowerBound * InflationFactor;

        // Gross ordinary-plus-gains income up to which long-term gains are taxed at 0%.
        public double ZeroRateCeilingGross => StandardDeduction + GainsBrackets.First(b => b.Rate > 0).LowerBound * InflationFactor;

        // How many more gain dollars would still be taxed at 0%, on top of this ordinary income and these gains.
        public double ZeroRateGainRoom(double ordinaryIncome, double gains) =>
            Math.Max(0, ZeroRateCeilingGross - ordinaryIncome - gains);

        // How many of `gains` are taxed at 0% on top of this ordinary income: the ones below the 0% ceiling.
        public double ZeroRateGains(double ordinaryIncome, double gains) =>
            Math.Min(Math.Max(0, gains), Math.Max(0, ZeroRateCeilingGross - ordinaryIncome));

        // Where the Tax Deferred fill and Roth conversions stop adding ordinary income. Normally the start of the
        // 22% bracket; but while this year's realized gains sit in the 0% band, extra ordinary income would push
        // them into 15% (stacking), so the fill stops where those gains would start crossing the 0% ceiling.
        public double OrdinaryFillCeiling(double ordinaryIncome, double realizedGains) =>
            realizedGains > 0 && ordinaryIncome < ZeroRateCeilingGross
                ? Math.Min(Bracket22CeilingGross, ZeroRateCeilingGross - realizedGains)
                : Bracket22CeilingGross;

        // Ordinary plus capital gains tax on the year's total income.
        public double TotalTax(double ordinaryIncome, double gains) => OrdinaryTax(ordinaryIncome) + CapitalGainsTax(ordinaryIncome, gains);

        // Long-term capital gains tax on `gains` stacked on top of `ordinaryIncome`: deduction the ordinary income
        // didn't use shelters gains first, and the rest is taxed from where ordinary taxable income ends.
        public double CapitalGainsTax(double ordinaryIncome, double gains)
        {
            double tax = 0;
            foreach (var (lower, upper, rate) in GainSegments(ordinaryIncome))
            {
                if (gains <= lower) break;
                tax += (Math.Min(gains, upper) - lower) * rate;
            }
            return tax;
        }

        // What selling an entire Brokerage balance would net after the capital gains tax on its embedded gain,
        // stacked on this year's ordinary income and gains already realized.
        public double BrokerageNetCapacity(double balance, double gainFraction, double ordinaryIncome, double gainsSoFar)
        {
            double sale = Math.Max(0, balance);
            double gain = sale * Math.Clamp(gainFraction, 0.0, 1.0);
            return sale - (CapitalGainsTax(ordinaryIncome, gainsSoFar + gain) - CapitalGainsTax(ordinaryIncome, gainsSoFar));
        }

        // Computes the gross Brokerage sale that nets `desiredNet` after capital gains tax, where each sold dollar
        // realizes `gainFraction` of gain (the rest is a tax-free return of basis), stacked on this year's
        // ordinary income and gains already realized. Within each gains segment every gross dollar nets
        // 1 - gainFraction * rate, so - like GrossUpOrdinary - this is an exact walk, no iteration.
        public double GrossUpBrokerageSale(double desiredNet, double gainFraction, double ordinaryIncome, double gainsSoFar)
        {
            if (desiredNet <= 0) return 0;
            double gf = Math.Clamp(gainFraction, 0.0, 1.0);
            if (gf == 0) return desiredNet;

            double remainingNet = desiredNet;
            double sale = 0;
            foreach (var (lower, upper, rate) in GainSegments(ordinaryIncome))
            {
                if (gainsSoFar >= upper) continue;

                double netPerGross = 1 - gf * rate;
                double segmentSale = (upper - Math.Max(lower, gainsSoFar)) / gf;
                double segmentNet = segmentSale * netPerGross;

                if (remainingNet <= segmentNet || double.IsPositiveInfinity(upper))
                    return sale + remainingNet / netPerGross;

                sale += segmentSale;
                remainingNet -= segmentNet;
            }
            return sale;
        }

        // The marginal long-term capital gains bracket at the top of this year's income stack - the rate the next
        // dollar of gain would face - and how many more gain dollars fit before the next LTCG bracket up. Mirrors
        // BracketRoom: gains exactly on a threshold belong to the lower bracket, and the next-bracket values are
        // null once already in the top bracket.
        public (double CurrentRate, double? AmountUntilNextBracket, double? NextRate) CapitalGainsBracketRoom(double ordinaryIncome, double gains)
        {
            var segments = GainSegments(ordinaryIncome).ToList();
            for (int i = 0; i < segments.Count; i++)
            {
                var (_, upper, rate) = segments[i];
                if (double.IsPositiveInfinity(upper)) return (rate, null, null);

                if (gains <= upper + 1e-6)
                {
                    // Skip segments at the same rate (the deduction shelter is a 0% segment ahead of the 0% bracket)
                    int next = i + 1;
                    double until = upper - gains;
                    while (next < segments.Count && segments[next].Rate == rate)
                    {
                        if (double.IsPositiveInfinity(segments[next].Upper)) return (rate, null, null);
                        until = segments[next].Upper - gains;
                        next++;
                    }
                    return (rate, Math.Max(0, until), next < segments.Count ? segments[next].Rate : (double?)null);
                }
            }
            return (segments[^1].Rate, null, null);
        }

        // The year's gains schedule for this ordinary income, as (lower, upper, rate) segments measured in gain
        // dollars from zero: first any unused standard deduction at 0%, then the LTCG brackets from where
        // ordinary taxable income ends.
        private IEnumerable<(double Lower, double Upper, double Rate)> GainSegments(double ordinaryIncome)
        {
            double shelter = Math.Max(0, StandardDeduction - ordinaryIncome);
            double start = Math.Max(0, ordinaryIncome - StandardDeduction);
            if (shelter > 0) yield return (0, shelter, 0);

            foreach (var bracket in GainsBrackets)
            {
                double upper = bracket.UpperBound * InflationFactor;
                if (start >= upper) continue;
                double lower = Math.Max(bracket.LowerBound * InflationFactor, start);
                yield return (shelter + lower - start, shelter + upper - start, bracket.Rate);
            }
        }

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
