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
                FailureYears = new List<int?>(),
                HighestReturnYears = new List<int>(),
                HighestReturnValues = new List<double>(),
                LowestReturnYears = new List<int>(),
                LowestReturnValues = new List<double>()
            };

            var random = new Random();
            var outOfMoneyMessage = new StringBuilder();
            var allRates = new List<double>(parameters.Years * parameters.Iterations);

            // For reporting the last successful run
            List<double> lastBalances = null;
            List<double> lastAnnualReturns = null;
            List<double> lastAnnualWithdrawals = null;
            List<double> lastTaxableBalances = null;
            List<double> lastNontaxableBalances = null;
            List<double> lastTaxableWithdrawals = null;
            List<double> lastNontaxableWithdrawals = null;

            for (int i = 0; i < parameters.Iterations; i++)
            {
                double inflation = 0.025;
                double ss = 0;
                double currentWithdrawal = parameters.Withdrawal;

                // Split investment for tracking
                double taxable = parameters.InitialInvestment * 0.4;
                double nontaxable = parameters.InitialInvestment * 0.6;

                var rates = new List<double>(parameters.Years);
                var withdrawals = new List<double>(parameters.Years);
                var balances = new List<double>(parameters.Years);
                var annualReturns = new List<double>(parameters.Years);
                var taxableBalances = new List<double>(parameters.Years);
                var nontaxableBalances = new List<double>(parameters.Years);
                var taxableWithdrawals = new List<double>(parameters.Years);
                var nontaxableWithdrawals = new List<double>(parameters.Years);

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
                    double periodWithdrawal = currentWithdrawal - (ss * .8);
                    withdrawals.Add(periodWithdrawal);

                    // Apply returns
                    taxable *= (1 + interestRate);
                    nontaxable *= (1 + interestRate);

                    // Pro-rata withdrawal calculation
                    double totalBalance = taxable + nontaxable;
                    double taxableProportion = totalBalance > 0 ? taxable / totalBalance : 0;
                    double nontaxableProportion = totalBalance > 0 ? nontaxable / totalBalance : 0;

                    // Calculate grossed-up withdrawal from taxable (to net the correct after-tax amount)
                    double desiredTaxableWithdrawal = periodWithdrawal * taxableProportion;
                    double grossTaxableWithdrawal = desiredTaxableWithdrawal / 0.8; // gross up for 20% tax
                    double desiredNontaxableWithdrawal = periodWithdrawal * nontaxableProportion;

                    // Withdraw from each account
                    taxable -= grossTaxableWithdrawal;
                    nontaxable -= desiredNontaxableWithdrawal;

                    // Add new money (e.g., inheritance) as after-tax cash in the year it arrives
                    if (run == parameters.YearNewMoney)
                        nontaxable += parameters.NewMoney;

                    // Track balances
                    taxableBalances.Add(taxable);
                    nontaxableBalances.Add(nontaxable);
                    // Track withdrawals
                    taxableWithdrawals.Add(grossTaxableWithdrawal);
                    nontaxableWithdrawals.Add(desiredNontaxableWithdrawal);

                    // Calculate annual return for reporting
                    double annualReturn = (taxable + nontaxable) - (taxable / (1 + interestRate) + nontaxable / (1 + interestRate));
                    annualReturns.Add(annualReturn);

                    // Recombine for balance and next year
                    double endingBalance = taxable + nontaxable;
                    balances.Add(endingBalance);

                    if (endingBalance < 0 || taxable < 0 || nontaxable < 0)
                    {
                        result.YearsOutOfMoney.Add(run);
                        result.OutOfMoneyCount++;
                        failureYear = run;
                        for (int c = 0; c < rates.Count; c++)
                        {
                            outOfMoneyMessage.Append($"\nYear {c}\nRate of return: {rates[c]:P2} \nwithdrawal: {withdrawals[c]:C0}(taxable {taxableWithdrawals[c]:C0}, nontax {nontaxableWithdrawals[c]:C0}) \nbal: {balances[c]:C0} (tax {taxableBalances[c]:C0}, nontax {nontaxableBalances[c]:C0})\n");
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
                    lastNontaxableBalances = nontaxableBalances;
                    lastTaxableWithdrawals = taxableWithdrawals;
                    lastNontaxableWithdrawals = nontaxableWithdrawals;
                }

                result.EndingBalances.Add(balances[^1]);
                result.AverageAnnualReturns.Add(rates.Average());
                result.FailureYears.Add(failureYear);

                double highestReturn = rates.Max();
                double lowestReturn = rates.Min();
                result.HighestReturnYears.Add(rates.IndexOf(highestReturn));
                result.HighestReturnValues.Add(highestReturn);
                result.LowestReturnYears.Add(rates.IndexOf(lowestReturn));
                result.LowestReturnValues.Add(lowestReturn);
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
                LastNontaxableBalances = lastNontaxableBalances,
                LastTaxableWithdrawals = lastTaxableWithdrawals,
                LastNontaxableWithdrawals = lastNontaxableWithdrawals
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
