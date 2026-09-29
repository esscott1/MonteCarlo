using System.Text;

namespace MonteCarloSimulation.Core
{
    public static class MonteCarloEngine
    {
        public static SimulationRunOutput Run(SimulationParameters parameters) => Run(parameters, new Random());

        // Seedable entry point, so tests can run the same random sequence through two engines.
        internal static SimulationRunOutput Run(SimulationParameters parameters, Random random)
        {
            var outOfMoneyMessage = new StringBuilder();
            var allRates = new List<double>(parameters.Years * parameters.Iterations);
            var runs = new List<RunSummary>(parameters.Iterations);

            for (int i = 0; i < parameters.Iterations; i++)
            {
                double inflation = 0.025;
                double ss = 0;
                double currentWithdrawal = parameters.Withdrawal;
                double standardDeduction = parameters.AnnualStandardDeduction;
                double bracketInflationFactor = 1.0;

                double taxable = parameters.InitialTaxableBalance;
                double brokerage = parameters.InitialBrokerageBasis + parameters.InitialBrokerageUnrealizedGain;
                double brokerageBasis = parameters.InitialBrokerageBasis;
                double roth = parameters.InitialRothBasis + parameters.InitialRothUnrealizedGain;
                double rothBasis = parameters.InitialRothBasis;

                double ageAtStartYears = (DateOnly.FromDateTime(DateTime.Today).DayNumber - parameters.Birthdate.DayNumber) / 365.25;

                var years = new List<RunYearDetail>(parameters.Years);
                int? failureYear = null;

                for (int run = 0; run < parameters.Years; run++)
                {
                    if (run > 19) inflation = 0.01;

                    if (run == parameters.SocialSecurityYearsUntilStart)
                        ss = parameters.SocialSecurityAnnualAmount;
                    else if (run > parameters.SocialSecurityYearsUntilStart)
                        ss *= (1 + inflation);

                    double interestRate = GetRateBoxMullerTransform(parameters.Mean, parameters.StdDev, random);
                    allRates.Add(interestRate);

                    currentWithdrawal *= (1 + inflation);
                    standardDeduction *= (1 + inflation);
                    bracketInflationFactor *= (1 + inflation);
                    double periodWithdrawal = currentWithdrawal;

                    // Social Security is income in the year received: 85% of it is ordinary income stacked
                    // first in the brackets (so everything else - Tax Deferred draws, conversions - stacks on
                    // top of it), and its after-tax amount reduces what the portfolio must supply this year.
                    double ssTaxable = SsTaxableFraction * ss;
                    double ssTax = ComputeOrdinaryTax(ssTaxable, standardDeduction, bracketInflationFactor, FederalTaxBrackets.Single2026);
                    double ssNet = ss - ssTax;
                    double need = Math.Max(0, periodWithdrawal - ssNet);
                    double ssSurplus = Math.Max(0, ssNet - periodWithdrawal);

                    // Start-of-year (prior year-end) Tax Deferred balance - the basis RMDs are computed on
                    double taxableStartOfYear = taxable;

                    // Apply returns (basis is never grown - it only moves via contributions/withdrawals)
                    taxable *= (1 + interestRate);
                    brokerage *= (1 + interestRate);
                    roth *= (1 + interestRate);

                    // Age gate: Taxable and Roth-gains are locked until 59.5, mirroring real-world early-
                    // withdrawal restrictions. Brokerage and Roth contributions (basis) are always accessible.
                    double ageInYear = ageAtStartYears + run;
                    bool ageEligible = ageInYear >= 59.5;

                    double eligibleTaxable = ageEligible ? taxable : 0;
                    double eligibleRoth = ageEligible ? roth : rothBasis;

                    // Brokerage: only the embedded-gain fraction of a withdrawal is taxed, at a flat LTCG rate -
                    // the rest is a tax-free return of principal (average-cost basis, not per-lot tracking).
                    double gainFraction = brokerage > 0 ? (brokerage - brokerageBasis) / brokerage : 0;
                    gainFraction = Math.Clamp(gainFraction, 0.0, 1.0);

                    double desiredTaxableWithdrawal, grossTaxableWithdrawal;
                    double desiredBrokerageWithdrawal, grossBrokerageWithdrawal;
                    double desiredRothWithdrawal;
                    bool isShortfall;

                    if (parameters.WithdrawalStrategy == WithdrawalStrategy.TaxOptimized)
                    {
                        // Ordered draw: Tax Deferred up to the top of the 12% bracket, then Brokerage, then
                        // more Tax Deferred into the 22%+ brackets, then Roth as last resort. The 12% ceiling
                        // is a preference, not a hard cap - otherwise a run fails with Tax Deferred money still
                        // on the books once Brokerage and Roth run dry. Each draw is capped at what that bucket
                        // can net after tax, so no bucket goes negative; anything still unmet is a shortfall.
                        double remaining = need;

                        double ceilingGross = standardDeduction + Bracket22Floor * bracketInflationFactor;
                        double maxTaxableGross = Math.Min(Math.Max(0, ceilingGross - ssTaxable), Math.Max(0, eligibleTaxable));
                        double maxTaxableNet = maxTaxableGross - IncrementalOrdinaryTax(ssTaxable, maxTaxableGross, standardDeduction, bracketInflationFactor);
                        desiredTaxableWithdrawal = Math.Min(remaining, maxTaxableNet);
                        // Clamp: re-grossing a net derived from the full cap can overshoot it by float rounding,
                        // which would leave the bucket at -1e-10 and falsely trip the negative-balance failure.
                        grossTaxableWithdrawal = Math.Min(maxTaxableGross, GrossUpTaxableWithdrawal(
                            desiredTaxableWithdrawal, ssTaxable, standardDeduction, bracketInflationFactor, FederalTaxBrackets.Single2026));
                        remaining -= desiredTaxableWithdrawal;

                        double brokerageNetCapacity = Math.Max(0, brokerage) * (1 - gainFraction * LtcgRate);
                        desiredBrokerageWithdrawal = Math.Min(remaining, brokerageNetCapacity);
                        grossBrokerageWithdrawal = Math.Min(Math.Max(0, brokerage), GrossUpLtcgWithdrawal(desiredBrokerageWithdrawal, gainFraction, LtcgRate));
                        remaining -= desiredBrokerageWithdrawal;

                        if (remaining > 0 && eligibleTaxable > 0)
                        {
                            double fullTaxableGross = eligibleTaxable;
                            double fullTaxableNet = fullTaxableGross - IncrementalOrdinaryTax(ssTaxable, fullTaxableGross, standardDeduction, bracketInflationFactor);
                            double totalTaxableNet = Math.Min(desiredTaxableWithdrawal + remaining, fullTaxableNet);
                            remaining -= totalTaxableNet - desiredTaxableWithdrawal;
                            desiredTaxableWithdrawal = totalTaxableNet;
                            grossTaxableWithdrawal = Math.Min(fullTaxableGross, GrossUpTaxableWithdrawal(
                                desiredTaxableWithdrawal, ssTaxable, standardDeduction, bracketInflationFactor, FederalTaxBrackets.Single2026));
                        }

                        desiredRothWithdrawal = Math.Min(remaining, Math.Max(0, eligibleRoth));
                        remaining -= desiredRothWithdrawal;

                        isShortfall = remaining > 0.01;
                    }
                    else
                    {
                        // Pro-rata withdrawal calculation - restricted to what's currently accessible.
                        // Once ageEligible is true this reduces to exactly the unrestricted 3-way split.
                        double eligibleTotal = eligibleTaxable + brokerage + eligibleRoth;
                        double taxableProportion = eligibleTotal > 0 ? eligibleTaxable / eligibleTotal : 0;
                        double brokerageProportion = eligibleTotal > 0 ? brokerage / eligibleTotal : 0;
                        double rothProportion = eligibleTotal > 0 ? eligibleRoth / eligibleTotal : 0;

                        // If accessible money can't cover the desired withdrawal - even though locked funds
                        // still exist - cap what's actually withdrawn and treat the year as a failure below.
                        isShortfall = eligibleTotal < need;
                        double baseWithdrawal = Math.Min(need, eligibleTotal);

                        // Calculate grossed-up withdrawal from taxable (to net the correct after-tax amount),
                        // stacked on top of the taxable share of Social Security in the inflation-scaled brackets
                        desiredTaxableWithdrawal = baseWithdrawal * taxableProportion;
                        grossTaxableWithdrawal = GrossUpTaxableWithdrawal(
                            desiredTaxableWithdrawal, ssTaxable, standardDeduction, bracketInflationFactor, FederalTaxBrackets.Single2026);

                        desiredBrokerageWithdrawal = baseWithdrawal * brokerageProportion;
                        grossBrokerageWithdrawal = GrossUpLtcgWithdrawal(desiredBrokerageWithdrawal, gainFraction, LtcgRate);

                        desiredRothWithdrawal = baseWithdrawal * rothProportion;
                    }

                    double ordinaryTaxAmount = (grossTaxableWithdrawal - desiredTaxableWithdrawal) + ssTax;
                    double capitalGainsTaxAmount = grossBrokerageWithdrawal - desiredBrokerageWithdrawal;

                    // Withdraw from each account
                    taxable -= grossTaxableWithdrawal;
                    double brokerageBeforeWithdrawal = brokerage;
                    brokerage -= grossBrokerageWithdrawal;
                    brokerageBasis *= brokerageBeforeWithdrawal > 0 ? brokerage / brokerageBeforeWithdrawal : 0;
                    double rothBeforeWithdrawal = roth;
                    roth -= desiredRothWithdrawal;
                    rothBasis *= rothBeforeWithdrawal > 0 ? roth / rothBeforeWithdrawal : 0;

                    // Roth conversion: fill the remaining 10%/12% bracket room with Tax Deferred -> Roth,
                    // paying the tax by selling Brokerage. A conversion isn't a withdrawal, so the 59.5 gate
                    // doesn't apply; converted dollars count as Roth basis (IRS 5-year rule not modeled).
                    double rothConversion = 0;
                    double rothConversionTax = 0;
                    if (parameters.EnableRothConversions)
                    {
                        double ceilingGross = standardDeduction + Bracket22Floor * bracketInflationFactor;
                        double ordinaryIncomeSoFar = ssTaxable + grossTaxableWithdrawal;
                        rothConversion = Math.Min(Math.Max(0, ceilingGross - ordinaryIncomeSoFar), Math.Max(0, taxable));
                        rothConversionTax = IncrementalOrdinaryTax(ordinaryIncomeSoFar, rothConversion, standardDeduction, bracketInflationFactor);

                        double conversionGainFraction = brokerage > 0 ? Math.Clamp((brokerage - brokerageBasis) / brokerage, 0.0, 1.0) : 0;
                        double maxTaxPayable = Math.Max(0, brokerage) * (1 - conversionGainFraction * LtcgRate);
                        if (rothConversionTax > maxTaxPayable)
                        {
                            // Tax on extra income is convex with f(0)=0, so f(k*x) <= k*f(x): scaling the
                            // conversion by k guarantees the resulting tax fits within maxTaxPayable.
                            rothConversion *= maxTaxPayable / rothConversionTax;
                            rothConversionTax = IncrementalOrdinaryTax(ordinaryIncomeSoFar, rothConversion, standardDeduction, bracketInflationFactor);
                        }

                        double taxSale = GrossUpLtcgWithdrawal(rothConversionTax, conversionGainFraction, LtcgRate);
                        double brokerageBeforeTaxSale = brokerage;
                        brokerage -= taxSale;
                        brokerageBasis *= brokerageBeforeTaxSale > 0 ? brokerage / brokerageBeforeTaxSale : 0;

                        taxable -= rothConversion;
                        roth += rothConversion;
                        rothBasis += rothConversion;

                        ordinaryTaxAmount += rothConversionTax;
                        capitalGainsTaxAmount += taxSale - rothConversionTax;
                        grossBrokerageWithdrawal += taxSale;
                    }

                    // Blended effective tax rate across all buckets (Roth always contributes 0)
                    double totalGrossWithdrawal = grossTaxableWithdrawal + grossBrokerageWithdrawal + desiredRothWithdrawal + ss;
                    double totalTax = ordinaryTaxAmount + capitalGainsTaxAmount;
                    double yearTaxRate = totalGrossWithdrawal > 0 ? totalTax / totalGrossWithdrawal : 0;

                    var (currentBracketRate, amountUntilNextBracket, nextBracketRate) = GetOrdinaryBracketRoom(
                        ssTaxable + grossTaxableWithdrawal + rothConversion, standardDeduction, bracketInflationFactor, FederalTaxBrackets.Single2026);

                    // Add new money (e.g., inheritance) as after-tax cash in the year it arrives -
                    // it's a cash contribution, not a gain, so it increases both balance and basis
                    if (run == parameters.YearNewMoney)
                    {
                        brokerage += parameters.NewMoney;
                        brokerageBasis += parameters.NewMoney;
                    }

                    // After-tax Social Security beyond this year's spending is saved to Brokerage as basis
                    brokerage += ssSurplus;
                    brokerageBasis += ssSurplus;

                    // Calculate annual return for reporting
                    double annualReturn = (taxable + brokerage + roth)
                        - (taxable / (1 + interestRate) + brokerage / (1 + interestRate) + roth / (1 + interestRate));

                    // Recombine for balance and next year
                    double endingBalance = taxable + brokerage + roth;

                    years.Add(new RunYearDetail
                    {
                        Year = run,
                        RateOfReturn = interestRate,
                        ReturnAmount = annualReturn,
                        Withdrawal = periodWithdrawal,
                        TaxableWithdrawal = grossTaxableWithdrawal,
                        BrokerageWithdrawal = grossBrokerageWithdrawal,
                        RothWithdrawal = desiredRothWithdrawal,
                        TaxRate = yearTaxRate,
                        Balance = endingBalance,
                        TaxableBalance = taxable,
                        BrokerageBalance = brokerage,
                        RothBalance = roth,
                        OrdinaryTaxAmount = ordinaryTaxAmount,
                        CapitalGainsTaxAmount = capitalGainsTaxAmount,
                        OrdinaryBracketRate = currentBracketRate,
                        AmountUntilNextBracket = amountUntilNextBracket,
                        NextBracketRate = nextBracketRate,
                        AgeEligible = ageEligible,
                        RothConversionAmount = rothConversion,
                        RothConversionTax = rothConversionTax,
                        AgeInYear = ageInYear,
                        TaxableWithdrawalPercentOfBalance = taxableStartOfYear > 0 ? grossTaxableWithdrawal / taxableStartOfYear : 0,
                        SocialSecurityIncome = ss,
                        SocialSecurityTax = ssTax
                    });

                    if (endingBalance < 0 || taxable < 0 || brokerage < 0 || roth < 0 || isShortfall)
                    {
                        failureYear = run;
                        foreach (var y in years)
                        {
                            outOfMoneyMessage.Append($"\nYear {y.Year}\nRate of return: {y.RateOfReturn:P2} \nwithdrawal: {y.Withdrawal:C0}(taxable {y.TaxableWithdrawal:C0}, brokerage {y.BrokerageWithdrawal:C0}, roth {y.RothWithdrawal:C0}) \ntax rate: {y.TaxRate:P2} \nbal: {y.Balance:C0} (tax {y.TaxableBalance:C0}, brokerage {y.BrokerageBalance:C0}, roth {y.RothBalance:C0})\n");
                        }
                        outOfMoneyMessage.Append('\n');
                        break;
                    }
                }

                runs.Add(new RunSummary { Years = years, FailureYear = failureYear });
            }

            return new SimulationRunOutput
            {
                Result = new SimulationResult { Runs = runs },
                AllRates = allRates,
                OutOfMoneyMessage = outOfMoneyMessage.ToString(),
                LastSuccessfulRun = runs.LastOrDefault(r => !r.Failed)?.Years
            };
        }

        private const double LtcgRate = 0.20;

        // Lower bound (un-inflated, above the standard deduction) of the first bracket at 22% or higher -
        // the ceiling Roth conversions fill up to.
        private static readonly double Bracket22Floor =
            FederalTaxBrackets.Single2026.First(b => b.Rate >= 0.22).LowerBound;

        // Taxable share of Social Security - a conservative simplification of the IRS provisional-income
        // formula (which taxes 0%, 50% or up to 85% depending on other income).
        private const double SsTaxableFraction = 0.85;

        // Ordinary tax owed on `extraIncome` stacked on top of `baseGrossIncome` already counted this year.
        private static double IncrementalOrdinaryTax(double baseGrossIncome, double extraIncome, double standardDeduction, double inflationFactor)
        {
            if (extraIncome <= 0) return 0;
            return ComputeOrdinaryTax(baseGrossIncome + extraIncome, standardDeduction, inflationFactor, FederalTaxBrackets.Single2026)
                - ComputeOrdinaryTax(baseGrossIncome, standardDeduction, inflationFactor, FederalTaxBrackets.Single2026);
        }

        // Forward tax calculation (the inverse of GrossUpTaxableWithdrawal): ordinary tax owed on
        // `grossIncome` after exempting `standardDeduction`, marginally through the inflation-scaled brackets.
        private static double ComputeOrdinaryTax(double grossIncome, double standardDeduction, double inflationFactor, IReadOnlyList<TaxBracket> brackets)
        {
            double taxableIncome = Math.Max(0, grossIncome - standardDeduction);
            double tax = 0;

            foreach (var bracket in brackets)
            {
                double lower = bracket.LowerBound * inflationFactor;
                double upper = bracket.UpperBound * inflationFactor;
                if (taxableIncome <= lower) break;
                tax += (Math.Min(taxableIncome, upper) - lower) * bracket.Rate;
            }

            return tax;
        }

        // Computes the pre-tax ("gross") Brokerage withdrawal that nets `desiredNet` dollars after tax,
        // taxing only the `gainFraction` portion of each withdrawn dollar at a flat `ltcgRate` - the
        // remaining (basis) portion is a tax-free return of principal. Unlike the taxable-bucket
        // gross-up, this is flat-rate: no brackets, no standard-deduction exemption.
        private static double GrossUpLtcgWithdrawal(double desiredNet, double gainFraction, double ltcgRate)
        {
            if (desiredNet <= 0) return 0;
            double taxableFraction = Math.Clamp(gainFraction, 0.0, 1.0) * ltcgRate;
            return desiredNet / (1 - taxableFraction);
        }

        // Computes the pre-tax ("gross") taxable-side withdrawal that nets `desiredNet` dollars after tax,
        // stacked on top of `baseIncome` ordinary income already counted this year (the taxable share of
        // Social Security). Whatever standard deduction the base didn't use is tax-free; the rest is taxed
        // marginally from where the base left off in the inflation-scaled brackets. Each bracket is linear,
        // so this is an exact analytic inversion — no iteration required. baseIncome = 0 is the plain case.
        private static double GrossUpTaxableWithdrawal(
            double desiredNet, double baseIncome, double standardDeduction, double inflationFactor, IReadOnlyList<TaxBracket> brackets)
        {
            double deductionRoom = Math.Max(0, standardDeduction - baseIncome);
            if (desiredNet <= deductionRoom) return desiredNet;

            double remainingNet = desiredNet - deductionRoom;
            double position = Math.Max(0, baseIncome - standardDeduction);
            double grossAboveDeduction = 0;

            foreach (var bracket in brackets)
            {
                double lower = bracket.LowerBound * inflationFactor;
                double upper = bracket.UpperBound * inflationFactor;
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

        // Finds the marginal ordinary-income bracket the current gross taxable withdrawal falls into,
        // and how many more gross withdrawal dollars could be taken before crossing into the next bracket
        // up. Mirrors GrossUpTaxableWithdrawal's accounting: the amount above the standard deduction maps
        // 1:1 onto the (inflation-scaled) bracket thresholds, so no re-inversion is needed here.
        // AmountUntilNextBracket/NextBracketRate are null once already in the top bracket, since there's
        // no next bracket to reach; CurrentBracketRate is always set (0 if the withdrawal never exceeds
        // the standard deduction, matching the first bracket's floor).
        private static (double CurrentBracketRate, double? AmountUntilNextBracket, double? NextBracketRate) GetOrdinaryBracketRoom(
            double grossTaxableWithdrawal, double standardDeduction, double inflationFactor, IReadOnlyList<TaxBracket> brackets)
        {
            double grossAboveDeduction = Math.Max(0, grossTaxableWithdrawal - standardDeduction);

            for (int i = 0; i < brackets.Count; i++)
            {
                double upper = brackets[i].UpperBound * inflationFactor;
                if (double.IsPositiveInfinity(upper)) return (brackets[i].Rate, null, null);

                // Income sitting exactly at a threshold (e.g. a conversion that fills to it) belongs to the
                // lower bracket - no dollar was taxed at the next rate. Tolerance absorbs float rounding.
                if (grossAboveDeduction <= upper + 1e-6)
                {
                    double? nextRate = i + 1 < brackets.Count ? brackets[i + 1].Rate : (double?)null;
                    return (brackets[i].Rate, Math.Max(0, upper - grossAboveDeduction), nextRate);
                }
            }

            return (brackets[^1].Rate, null, null);
        }

        private static double GetRateBoxMullerTransform(double mean, double standardDeviation, Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + standardDeviation * randStdNormal;
        }
    }
}
