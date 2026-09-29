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
            var strategy = WithdrawalStrategies.For(parameters.WithdrawalStrategy);

            for (int i = 0; i < parameters.Iterations; i++)
            {
                double inflation = 0.025;
                double ss = 0;
                double currentWithdrawal = parameters.Withdrawal;
                double standardDeduction = parameters.AnnualStandardDeduction;
                double bracketInflationFactor = 1.0;

                var accounts = Accounts.FromParameters(parameters);

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
                    var taxYear = new TaxYear(standardDeduction, bracketInflationFactor, FederalTaxBrackets.Single2026);

                    // Social Security is income in the year received: 85% of it is ordinary income stacked
                    // first in the brackets (so everything else - Tax Deferred draws, conversions - stacks on
                    // top of it), and its after-tax amount reduces what the portfolio must supply this year.
                    double ssTaxable = TaxAssumptions.SsTaxableFraction * ss;
                    double ssTax = taxYear.OrdinaryTax(ssTaxable);
                    double ssNet = ss - ssTax;
                    double need = Math.Max(0, periodWithdrawal - ssNet);
                    double ssSurplus = Math.Max(0, ssNet - periodWithdrawal);

                    // Start-of-year (prior year-end) Tax Deferred balance - the basis RMDs are computed on
                    double taxableStartOfYear = accounts.Taxable;

                    accounts.ApplyReturn(interestRate);

                    double ageInYear = ageAtStartYears + run;
                    bool ageEligible = ageInYear >= 59.5;

                    double eligibleTaxable = accounts.EligibleTaxable(ageEligible);
                    double eligibleRoth = accounts.EligibleRoth(ageEligible);
                    double brokerage = accounts.Brokerage;
                    double gainFraction = accounts.BrokerageGainFraction;

                    var plan = strategy.Plan(new WithdrawalContext(need, ssTaxable, taxYear, eligibleTaxable, brokerage, gainFraction, eligibleRoth));
                    double grossTaxableWithdrawal = plan.GrossTaxable;
                    double grossBrokerageWithdrawal = plan.GrossBrokerage;
                    double desiredRothWithdrawal = plan.Roth;
                    bool isShortfall = plan.IsShortfall;

                    double ordinaryTaxAmount = plan.TaxableTax + ssTax;
                    double capitalGainsTaxAmount = plan.CapitalGainsTax;

                    accounts.WithdrawTaxable(grossTaxableWithdrawal);
                    accounts.SellBrokerage(grossBrokerageWithdrawal);
                    accounts.WithdrawRoth(desiredRothWithdrawal);

                    // Roth conversion: fill the remaining 10%/12% bracket room with Tax Deferred -> Roth,
                    // paying the tax by selling Brokerage. A conversion isn't a withdrawal, so the 59.5 gate
                    // doesn't apply; converted dollars count as Roth basis (IRS 5-year rule not modeled).
                    double rothConversion = 0;
                    double rothConversionTax = 0;
                    if (parameters.EnableRothConversions)
                    {
                        double ordinaryIncomeSoFar = ssTaxable + grossTaxableWithdrawal;
                        rothConversion = Math.Min(Math.Max(0, taxYear.Bracket22CeilingGross - ordinaryIncomeSoFar), Math.Max(0, accounts.Taxable));
                        rothConversionTax = taxYear.IncrementalOrdinaryTax(ordinaryIncomeSoFar, rothConversion);

                        double conversionGainFraction = accounts.BrokerageGainFraction;
                        double maxTaxPayable = TaxAssumptions.BrokerageNetCapacity(accounts.Brokerage, conversionGainFraction);
                        if (rothConversionTax > maxTaxPayable)
                        {
                            // Tax on extra income is convex with f(0)=0, so f(k*x) <= k*f(x): scaling the
                            // conversion by k guarantees the resulting tax fits within maxTaxPayable.
                            rothConversion *= maxTaxPayable / rothConversionTax;
                            rothConversionTax = taxYear.IncrementalOrdinaryTax(ordinaryIncomeSoFar, rothConversion);
                        }

                        double taxSale = TaxAssumptions.GrossUpLtcg(rothConversionTax, conversionGainFraction);
                        accounts.SellBrokerage(taxSale);
                        accounts.ConvertToRoth(rothConversion);

                        ordinaryTaxAmount += rothConversionTax;
                        capitalGainsTaxAmount += taxSale - rothConversionTax;
                        grossBrokerageWithdrawal += taxSale;
                    }

                    // Blended effective tax rate across all buckets (Roth always contributes 0)
                    double totalGrossWithdrawal = grossTaxableWithdrawal + grossBrokerageWithdrawal + desiredRothWithdrawal + ss;
                    double totalTax = ordinaryTaxAmount + capitalGainsTaxAmount;
                    double yearTaxRate = totalGrossWithdrawal > 0 ? totalTax / totalGrossWithdrawal : 0;

                    var (currentBracketRate, amountUntilNextBracket, nextBracketRate) = taxYear.BracketRoom(
                        ssTaxable + grossTaxableWithdrawal + rothConversion);

                    // Add new money (e.g., inheritance) as after-tax cash in the year it arrives -
                    // it's a cash contribution, not a gain, so it increases both balance and basis
                    if (run == parameters.YearNewMoney)
                        accounts.DepositBrokerageCash(parameters.NewMoney);

                    // After-tax Social Security beyond this year's spending is saved to Brokerage as basis
                    accounts.DepositBrokerageCash(ssSurplus);

                    // Calculate annual return for reporting
                    double annualReturn = accounts.Total
                        - (accounts.Taxable / (1 + interestRate) + accounts.Brokerage / (1 + interestRate) + accounts.Roth / (1 + interestRate));

                    double endingBalance = accounts.Total;

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
                        TaxableBalance = accounts.Taxable,
                        BrokerageBalance = accounts.Brokerage,
                        RothBalance = accounts.Roth,
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

                    if (accounts.AnyNegative || isShortfall)
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

        private static double GetRateBoxMullerTransform(double mean, double standardDeviation, Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + standardDeviation * randStdNormal;
        }
    }
}
