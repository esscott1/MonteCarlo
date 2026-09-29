using System.Text;

namespace MonteCarloSimulation.Core
{
    public static class MonteCarloEngine
    {
        public static SimulationRunOutput Run(SimulationParameters parameters)
        {
            var result = new SimulationResult
            {
                OutOfMoneyCount = 0,
                YearsOutOfMoney = new List<int>(),
                FailedScenarioAverages = new List<double>(),
                SuccessMoneyRemaining = new List<double>(),
                EndingBalances = new List<double>(),
                AverageAnnualReturns = new List<double>(),
                AverageTaxRates = new List<double>(),
                FailureYears = new List<int?>(),
                HighestReturnYears = new List<int>(),
                HighestReturnValues = new List<double>(),
                LowestReturnYears = new List<int>(),
                LowestReturnValues = new List<double>(),
                LowestBalanceYears = new List<int>(),
                LowestBalanceValues = new List<double>(),
                RunDetails = new List<List<RunYearDetail>>()
            };

            var random = new Random();
            var outOfMoneyMessage = new StringBuilder();
            var allRates = new List<double>(parameters.Years * parameters.Iterations);

            // For reporting the last successful run
            List<double> lastBalances = null;
            List<double> lastAnnualReturns = null;
            List<double> lastAnnualWithdrawals = null;
            List<double> lastTaxableBalances = null;
            List<double> lastBrokerageBalances = null;
            List<double> lastRothBalances = null;
            List<double> lastTaxableWithdrawals = null;
            List<double> lastBrokerageWithdrawals = null;
            List<double> lastRothWithdrawals = null;
            List<double> lastTaxRates = null;
            List<double> lastOrdinaryTaxAmounts = null;
            List<double> lastCapitalGainsTaxAmounts = null;
            List<double> lastOrdinaryBracketRates = null;
            List<double?> lastAmountsUntilNextBracket = null;
            List<double?> lastNextBracketRates = null;
            List<double> lastRothConversionAmounts = null;
            List<double> lastRothConversionTaxes = null;
            List<double> lastAgesInYear = null;
            List<double> lastTaxableWithdrawalPercents = null;

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

                var rates = new List<double>(parameters.Years);
                var withdrawals = new List<double>(parameters.Years);
                var balances = new List<double>(parameters.Years);
                var annualReturns = new List<double>(parameters.Years);
                var taxableBalances = new List<double>(parameters.Years);
                var brokerageBalances = new List<double>(parameters.Years);
                var rothBalances = new List<double>(parameters.Years);
                var taxableWithdrawals = new List<double>(parameters.Years);
                var brokerageWithdrawals = new List<double>(parameters.Years);
                var rothWithdrawals = new List<double>(parameters.Years);
                var taxRates = new List<double>(parameters.Years);
                var ordinaryTaxAmounts = new List<double>(parameters.Years);
                var capitalGainsTaxAmounts = new List<double>(parameters.Years);
                var ordinaryBracketRates = new List<double>(parameters.Years);
                var amountsUntilNextBracket = new List<double?>(parameters.Years);
                var nextBracketRates = new List<double?>(parameters.Years);
                var ageEligibleFlags = new List<bool>(parameters.Years);
                var agesInYear = new List<double>(parameters.Years);
                var taxableWithdrawalPercents = new List<double>(parameters.Years);
                var rothConversionAmounts = new List<double>(parameters.Years);
                var rothConversionTaxes = new List<double>(parameters.Years);

                int? failureYear = null;

                for (int run = 0; run < parameters.Years; run++)
                {
                    if (run > 19) inflation = 0.01;

                    if (run == parameters.SocialSecurityYearsUntilStart)
                        ss = parameters.SocialSecurityAnnualAmount;
                    else if (run > parameters.SocialSecurityYearsUntilStart)
                        ss *= (1 + inflation);

                    double interestRate = GetRateBoxMullerTransform(parameters.Mean, parameters.StdDev, random);
                    rates.Add(interestRate);
                    allRates.Add(interestRate);

                    currentWithdrawal *= (1 + inflation);
                    standardDeduction *= (1 + inflation);
                    bracketInflationFactor *= (1 + inflation);
                    double periodWithdrawal = currentWithdrawal;
                    withdrawals.Add(periodWithdrawal);

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
                    ageEligibleFlags.Add(ageEligible);
                    agesInYear.Add(ageInYear);

                    // Pro-rata withdrawal calculation - restricted to what's currently accessible.
                    // Once ageEligible is true this reduces to exactly the unrestricted 3-way split.
                    double eligibleTaxable = ageEligible ? taxable : 0;
                    double eligibleRoth = ageEligible ? roth : rothBasis;
                    double eligibleTotal = eligibleTaxable + brokerage + eligibleRoth;
                    double taxableProportion = eligibleTotal > 0 ? eligibleTaxable / eligibleTotal : 0;
                    double brokerageProportion = eligibleTotal > 0 ? brokerage / eligibleTotal : 0;
                    double rothProportion = eligibleTotal > 0 ? eligibleRoth / eligibleTotal : 0;

                    // If accessible money can't cover the desired withdrawal - even though locked funds
                    // still exist - cap what's actually withdrawn and treat the year as a failure below.
                    bool isShortfall = eligibleTotal < periodWithdrawal;
                    double baseWithdrawal = Math.Min(periodWithdrawal, eligibleTotal);

                    // Calculate grossed-up withdrawal from taxable (to net the correct after-tax amount),
                    // exempting the first `standardDeduction` dollars of this withdrawal from tax,
                    // then taxing the remainder marginally through the inflation-scaled bracket table
                    double desiredTaxableWithdrawal = baseWithdrawal * taxableProportion;
                    double grossTaxableWithdrawal = GrossUpTaxableWithdrawal(
                        desiredTaxableWithdrawal, standardDeduction, bracketInflationFactor, FederalTaxBrackets.Single2026);

                    // Brokerage: only the embedded-gain fraction of the withdrawal is taxed, at a flat LTCG rate -
                    // the rest is a tax-free return of principal (average-cost basis, not per-lot tracking).
                    double gainFraction = brokerage > 0 ? (brokerage - brokerageBasis) / brokerage : 0;
                    gainFraction = Math.Clamp(gainFraction, 0.0, 1.0);
                    double desiredBrokerageWithdrawal = baseWithdrawal * brokerageProportion;
                    double grossBrokerageWithdrawal = GrossUpLtcgWithdrawal(desiredBrokerageWithdrawal, gainFraction, LtcgRate);

                    double desiredRothWithdrawal = baseWithdrawal * rothProportion;

                    double ordinaryTaxAmount = grossTaxableWithdrawal - desiredTaxableWithdrawal;
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
                        rothConversion = Math.Min(Math.Max(0, ceilingGross - grossTaxableWithdrawal), Math.Max(0, taxable));
                        rothConversionTax = ConversionTax(grossTaxableWithdrawal, rothConversion, standardDeduction, bracketInflationFactor);

                        double conversionGainFraction = brokerage > 0 ? Math.Clamp((brokerage - brokerageBasis) / brokerage, 0.0, 1.0) : 0;
                        double maxTaxPayable = Math.Max(0, brokerage) * (1 - conversionGainFraction * LtcgRate);
                        if (rothConversionTax > maxTaxPayable)
                        {
                            // Tax on extra income is convex with f(0)=0, so f(k*x) <= k*f(x): scaling the
                            // conversion by k guarantees the resulting tax fits within maxTaxPayable.
                            rothConversion *= maxTaxPayable / rothConversionTax;
                            rothConversionTax = ConversionTax(grossTaxableWithdrawal, rothConversion, standardDeduction, bracketInflationFactor);
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
                    rothConversionAmounts.Add(rothConversion);
                    rothConversionTaxes.Add(rothConversionTax);

                    // Blended effective tax rate across all buckets (Roth always contributes 0)
                    double totalGrossWithdrawal = grossTaxableWithdrawal + grossBrokerageWithdrawal + desiredRothWithdrawal;
                    double totalTax = ordinaryTaxAmount + capitalGainsTaxAmount;
                    double yearTaxRate = totalGrossWithdrawal > 0 ? totalTax / totalGrossWithdrawal : 0;
                    taxRates.Add(yearTaxRate);

                    ordinaryTaxAmounts.Add(ordinaryTaxAmount);
                    capitalGainsTaxAmounts.Add(capitalGainsTaxAmount);

                    var (currentBracketRate, amountUntilNextBracket, nextBracketRate) = GetOrdinaryBracketRoom(
                        grossTaxableWithdrawal + rothConversion, standardDeduction, bracketInflationFactor, FederalTaxBrackets.Single2026);
                    ordinaryBracketRates.Add(currentBracketRate);
                    amountsUntilNextBracket.Add(amountUntilNextBracket);
                    nextBracketRates.Add(nextBracketRate);

                    // Add new money (e.g., inheritance) as after-tax cash in the year it arrives -
                    // it's a cash contribution, not a gain, so it increases both balance and basis
                    if (run == parameters.YearNewMoney)
                    {
                        brokerage += parameters.NewMoney;
                        brokerageBasis += parameters.NewMoney;
                    }

                    // Add Social Security as taxable income in the years it's received
                    taxable += ss;

                    // Track balances
                    taxableBalances.Add(taxable);
                    brokerageBalances.Add(brokerage);
                    rothBalances.Add(roth);
                    // Track withdrawals
                    taxableWithdrawals.Add(grossTaxableWithdrawal);
                    taxableWithdrawalPercents.Add(taxableStartOfYear > 0 ? grossTaxableWithdrawal / taxableStartOfYear : 0);
                    brokerageWithdrawals.Add(grossBrokerageWithdrawal);
                    rothWithdrawals.Add(desiredRothWithdrawal);

                    // Calculate annual return for reporting
                    double annualReturn = (taxable + brokerage + roth)
                        - (taxable / (1 + interestRate) + brokerage / (1 + interestRate) + roth / (1 + interestRate));
                    annualReturns.Add(annualReturn);

                    // Recombine for balance and next year
                    double endingBalance = taxable + brokerage + roth;
                    balances.Add(endingBalance);

                    if (endingBalance < 0 || taxable < 0 || brokerage < 0 || roth < 0 || isShortfall)
                    {
                        result.YearsOutOfMoney.Add(run);
                        result.OutOfMoneyCount++;
                        failureYear = run;
                        for (int c = 0; c < rates.Count; c++)
                        {
                            outOfMoneyMessage.Append($"\nYear {c}\nRate of return: {rates[c]:P2} \nwithdrawal: {withdrawals[c]:C0}(taxable {taxableWithdrawals[c]:C0}, brokerage {brokerageWithdrawals[c]:C0}, roth {rothWithdrawals[c]:C0}) \ntax rate: {taxRates[c]:P2} \nbal: {balances[c]:C0} (tax {taxableBalances[c]:C0}, brokerage {brokerageBalances[c]:C0}, roth {rothBalances[c]:C0})\n");
                        }
                        outOfMoneyMessage.Append('\n');
                        result.FailedScenarioAverages.Add(rates.Average());
                        break;
                    }

                    // Only store the last successful run for reporting
                    result.SuccessMoneyRemaining.Add(balances[^1]);
                    lastBalances = balances;
                    lastAnnualReturns = annualReturns;
                    lastAnnualWithdrawals = withdrawals;
                    lastTaxableBalances = taxableBalances;
                    lastBrokerageBalances = brokerageBalances;
                    lastRothBalances = rothBalances;
                    lastTaxableWithdrawals = taxableWithdrawals;
                    lastBrokerageWithdrawals = brokerageWithdrawals;
                    lastRothWithdrawals = rothWithdrawals;
                    lastTaxRates = taxRates;
                    lastOrdinaryTaxAmounts = ordinaryTaxAmounts;
                    lastCapitalGainsTaxAmounts = capitalGainsTaxAmounts;
                    lastOrdinaryBracketRates = ordinaryBracketRates;
                    lastAmountsUntilNextBracket = amountsUntilNextBracket;
                    lastNextBracketRates = nextBracketRates;
                    lastRothConversionAmounts = rothConversionAmounts;
                    lastRothConversionTaxes = rothConversionTaxes;
                    lastAgesInYear = agesInYear;
                    lastTaxableWithdrawalPercents = taxableWithdrawalPercents;
                }

                result.EndingBalances.Add(balances[^1]);
                result.AverageAnnualReturns.Add(rates.Average());
                result.AverageTaxRates.Add(taxRates.Average());
                result.FailureYears.Add(failureYear);

                double highestReturn = rates.Max();
                double lowestReturn = rates.Min();
                result.HighestReturnYears.Add(rates.IndexOf(highestReturn));
                result.HighestReturnValues.Add(highestReturn);
                result.LowestReturnYears.Add(rates.IndexOf(lowestReturn));
                result.LowestReturnValues.Add(lowestReturn);

                double lowestBalance = balances.Min();
                result.LowestBalanceYears.Add(balances.IndexOf(lowestBalance));
                result.LowestBalanceValues.Add(lowestBalance);

                var runDetails = new List<RunYearDetail>(rates.Count);
                for (int c = 0; c < rates.Count; c++)
                {
                    runDetails.Add(new RunYearDetail(
                        c, rates[c], annualReturns[c], withdrawals[c],
                        taxableWithdrawals[c], brokerageWithdrawals[c], rothWithdrawals[c],
                        taxRates[c], balances[c], taxableBalances[c], brokerageBalances[c], rothBalances[c],
                        ordinaryTaxAmounts[c], capitalGainsTaxAmounts[c], ordinaryBracketRates[c], amountsUntilNextBracket[c], nextBracketRates[c],
                        ageEligibleFlags[c], rothConversionAmounts[c], rothConversionTaxes[c],
                        agesInYear[c], taxableWithdrawalPercents[c]));
                }
                result.RunDetails.Add(runDetails);
            }

            return new SimulationRunOutput
            {
                Result = result,
                AllRates = allRates,
                OutOfMoneyMessage = outOfMoneyMessage.ToString(),
                LastBalances = lastBalances,
                LastAnnualReturns = lastAnnualReturns,
                LastAnnualWithdrawals = lastAnnualWithdrawals,
                LastTaxableBalances = lastTaxableBalances,
                LastBrokerageBalances = lastBrokerageBalances,
                LastRothBalances = lastRothBalances,
                LastTaxableWithdrawals = lastTaxableWithdrawals,
                LastBrokerageWithdrawals = lastBrokerageWithdrawals,
                LastRothWithdrawals = lastRothWithdrawals,
                LastTaxRates = lastTaxRates,
                LastOrdinaryTaxAmounts = lastOrdinaryTaxAmounts,
                LastCapitalGainsTaxAmounts = lastCapitalGainsTaxAmounts,
                LastOrdinaryBracketRates = lastOrdinaryBracketRates,
                LastAmountsUntilNextBracket = lastAmountsUntilNextBracket,
                LastNextBracketRates = lastNextBracketRates,
                LastRothConversionAmounts = lastRothConversionAmounts,
                LastRothConversionTaxes = lastRothConversionTaxes,
                LastAgesInYear = lastAgesInYear,
                LastTaxableWithdrawalPercents = lastTaxableWithdrawalPercents
            };
        }

        private const double LtcgRate = 0.20;

        // Lower bound (un-inflated, above the standard deduction) of the first bracket at 22% or higher -
        // the ceiling Roth conversions fill up to.
        private static readonly double Bracket22Floor =
            FederalTaxBrackets.Single2026.First(b => b.Rate >= 0.22).LowerBound;

        private static double ConversionTax(double baseGrossIncome, double conversion, double standardDeduction, double inflationFactor)
        {
            if (conversion <= 0) return 0;
            return ComputeOrdinaryTax(baseGrossIncome + conversion, standardDeduction, inflationFactor, FederalTaxBrackets.Single2026)
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
        // exempting the first `standardDeduction` dollars from tax and taxing the remainder marginally
        // through `brackets` (thresholds scaled by `inflationFactor` to match the already-inflated deduction).
        // Each bracket is linear, so this is an exact analytic inversion — no iteration required.
        private static double GrossUpTaxableWithdrawal(
            double desiredNet, double standardDeduction, double inflationFactor, IReadOnlyList<TaxBracket> brackets)
        {
            if (desiredNet <= standardDeduction) return desiredNet;

            double remainingNet = desiredNet - standardDeduction;
            double grossAboveDeduction = 0;

            foreach (var bracket in brackets)
            {
                double lower = bracket.LowerBound * inflationFactor;
                double upper = bracket.UpperBound * inflationFactor;
                double bracketWidth = upper - lower;
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

            return standardDeduction + grossAboveDeduction;
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
