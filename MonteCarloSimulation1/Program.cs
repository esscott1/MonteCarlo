using MonteCarloSimulation.Core;

namespace MonteCarloSimulation1
{
    public class MonteCarloSimulation
    {
        public static void Main(string[] args)
        {
            while (true)
            {
                Console.Write("Type 'end' to exit or press Enter to start a new simulation: ");
                string startInput = Console.ReadLine();
                if (startInput != null && startInput.Trim().Equals("end", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Exiting simulation.");
                    break;
                }

                var parameters = SimulationPrompt.PromptAllParameters();
                var output = MonteCarloEngine.Run(parameters);
                SimulationReporter.PrintResults(parameters.ScenarioDescription, output, parameters);
            }
        }
    }

    public static class SimulationReporter
    {
        public static void PrintResults(
            string scenarioDescription,
            SimulationRunOutput output,
            SimulationParameters parameters)

        // ---- OUTPUT SECTION: Print after all iterations ----
        {
            var result = output.Result;
            string order = output.WithdrawalStrategy == WithdrawalStrategy.ProRata ? "Pro-rata" : "Tax-optimized";
            string conversionTax = parameters.EnableRothConversions && parameters.ConversionTaxFunding == ConversionTaxFunding.FromConversion
                ? "; Roth conversion tax is paid out of the converted amount"
                : "";
            Console.WriteLine($"\nAccounts drawn in the {order} order (the better of the two for these inputs, chosen by the app){conversionTax}.");
            if (result.OutOfMoneyCount > 0)
            {
                Console.WriteLine(output.OutOfMoneyMessage);
                Console.WriteLine("----------------------------------------------------");
                double survival = 1 - (result.OutOfMoneyCount / (double)parameters.Iterations);
                if (survival > 0.8)
                    Smile();
                else
                    Frown();
                Console.WriteLine($"\n{result.OutOfMoneyCount} portfolios did not survive {parameters.Years} years given {parameters.Iterations} iterations. survival rate: {survival:P4}");
                Console.WriteLine($"\nThe Scenario: {scenarioDescription} with Initial mean: {parameters.Mean:P4}  Initial standard deviation: {parameters.StdDev:P4}");

                double totalAvgRates = output.AllRates.Average();
                double variance = output.AllRates.Average(n => Math.Pow(n - totalAvgRates, 2));
                double stdDev = Math.Sqrt(variance);
                Console.WriteLine($"\nActual total avg return {totalAvgRates:P4} with std dev {stdDev} based on {scenarioDescription} into Randomization");
                Console.WriteLine($"\nInheritance of  {parameters.NewMoney:C0} in  {parameters.RetirementDate.Year + parameters.YearNewMoney} was considered");

                Console.WriteLine($"\nAverage year of failures ran out of money in year {result.YearsOutOfMoney.Average():F0} with an Avg return of {result.FailedScenarioAverages.Average():P4}");
                Console.WriteLine($"\nSee Above for failed scenarios and their rates of return, withdrawals, and balances.\n");
            }
            else //all scenarios succeeded
            {
                Console.WriteLine();
                Console.WriteLine($"\nlast run balances: ");
                if (output.LastSuccessfulRun != null)
                {
                    foreach (var y in output.LastSuccessfulRun.Skip(1))
                    {
                        Console.WriteLine(
                            $"Year {y.CalendarYear}{(y.YearFraction < 0.9995 ? $" (partial, {y.YearFraction:P0} of year)" : "")}\n withdrawals: {y.Withdrawal:C0}, (" +
                            $"taxable: {y.TaxableWithdrawal:C0}, " +
                            $"brokerage: {y.BrokerageWithdrawal:C0}, " +
                            $"roth: {y.RothWithdrawal:C0})\n " +
                            $"tax rate: {y.TaxRate:P2}\n " +
                            $"capital gains: {y.CapitalGainsTaxAmount:C0} ({y.CapitalGainsBracketRate:P0} LTCG bracket)" +
                            (y.HarvestedGains > 0 ? $", harvested {y.HarvestedGains:C0} at 0%" : "") + "\n " +
                            $"roth conversion: {y.RothConversionAmount:C0} (tax {y.RothConversionTax:C0})\n " +
                            $"social security: {y.SocialSecurityIncome:C0} ({y.SocialSecurityMonths} mo, tax {y.SocialSecurityTax:C0})\n " +
                            $"years return ($): {y.ReturnAmount:C0}\n " +
                            $"total balance: {y.Balance:C0} (" +
                            $"taxable balance: {y.TaxableBalance:C0}, " +
                            $"brokerage balance: {y.BrokerageBalance:C0}, " +
                            $"roth balance: {y.RothBalance:C0}), ");
                    }
                }
                Smile();
                Console.WriteLine($"***  All scenarios survived! ***");
                Console.WriteLine($"\nScenario: {scenarioDescription} with Initial mean: {parameters.Mean:P4}  Initial standard deviation: {parameters.StdDev:P4}");
                Console.WriteLine($"\nInitial mean: {parameters.Mean:P4}  Initial standard deviation: {parameters.StdDev:P4}");
                Console.WriteLine($"\nAverage balance remaining: {result.SuccessMoneyRemaining.Average():C0}");
                Console.WriteLine();
            }

            Console.WriteLine("\nPer-run summary (ending balance and average annual return):");
            for (int r = 0; r < result.Runs.Count; r++)
            {
                var run = result.Runs[r];
                string failureNote = run.Failed
                    ? $", ran out of money in year {run.FailureYear}"
                    : "";
                Console.WriteLine(
                    $"Run {r + 1}: Ending balance: {run.EndingBalance:C0}, " +
                    $"Average annual return: {run.AverageAnnualReturn:P2}, " +
                    $"Average tax rate: {run.AverageTaxRate:P2}, " +
                    $"Highest return: year {run.HighestReturnYear} ({run.HighestReturnValue:P2}), " +
                    $"Lowest return: year {run.LowestReturnYear} ({run.LowestReturnValue:P2}), " +
                    $"Lowest balance: {run.LowestBalanceValue:C0} in year {run.LowestBalanceYear}{failureNote}");
            }

            Console.WriteLine("\nSimulation complete.");
        }
        static void Smile()
        {
            Console.WriteLine("\nMonte Carlo Simulation Results:");
            Console.WriteLine("  _____  ");
            Console.WriteLine(" /     \\ ");
            Console.WriteLine("|  o o  |");
            Console.WriteLine("|   ^   |");
            Console.WriteLine("|  '-'  |");
            Console.WriteLine(" \\_____/ \n");

        }
        static void Frown()
        {
            Console.WriteLine("\nMonte Carlo Simulation Results:");
            Console.WriteLine("  _____  ");
            Console.WriteLine(" /     \\ ");
            Console.WriteLine("|  o o  |");
            Console.WriteLine("|   ^   |");
            Console.WriteLine("|   _   |");
            Console.WriteLine("|  ' '  |");
            Console.WriteLine(" \\_____/ \n");

        }

    }

    public static class SimulationPrompt
    {
        public static SimulationParameters PromptAllParameters()
        {
            int option = PromptInvestmentOption();
            var scenario = InvestmentScenarios.ById(option) ?? InvestmentScenarios.All[3];

            int years = PromptYears();
            int iterations = PromptIterations();
            double withdrawal = PromptMonthlyWithdrawal() * 12; // the model works in annual amounts
            var birthdate = PromptBirthdate();
            var retirementDate = PromptRetirementDate(birthdate);

            return new SimulationParameters
            {
                Years = years,
                Iterations = iterations,
                Withdrawal = withdrawal,
                Birthdate = birthdate,
                RetirementDate = retirementDate,
                InitialTaxableBalance = PromptInitialTaxableBalance(),
                InitialRothBasis = PromptInitialRothBasis(),
                InitialRothUnrealizedGain = PromptInitialRothUnrealizedGain(),
                InitialBrokerageBasis = PromptInitialBrokerageBasis(),
                InitialBrokerageUnrealizedGain = PromptInitialBrokerageUnrealizedGain(),
                Mean = scenario.Mean,
                StdDev = scenario.StdDev,
                NewMoney = PromptNewMoney(),
                YearNewMoney = PromptYearNewMoney(),
                SocialSecurityStartDate = PromptSocialSecurityStartDate(birthdate),
                SocialSecurityMonthlyAmount = PromptSocialSecurityMonthlyAmount(),
                AnnualStandardDeduction = PromptAnnualStandardDeduction(),
                EnableRothConversions = PromptEnableRothConversions(),
                ScenarioDescription = scenario.Description
            };
        }

        public static int PromptInvestmentOption()
        {
            Console.WriteLine("Select an investment scenario:");
            foreach (var scenario in InvestmentScenarios.All)
            {
                Console.WriteLine($"{scenario.Id}. {scenario.MenuLabel}");
            }
            Console.Write($"Enter the number of your choice (1-{InvestmentScenarios.All.Count}): ");

            while (true)
            {
                string input = Console.ReadLine();
                if (int.TryParse(input, out int choice) && InvestmentScenarios.ById(choice) != null)
                {
                    return choice;
                }
                Console.Write($"Invalid input. Please enter a number between 1 and {InvestmentScenarios.All.Count}: ");
            }
        }

        public static int PromptYears()
        {
            Console.Write("How long do you want your money to last (years)? ");
            while (true)
            {
                string input = Console.ReadLine();
                if (int.TryParse(input, out int years) && years > 0)
                {
                    return years;
                }
                Console.Write("Invalid input. Please enter a positive integer for years: ");
            }
        }

        public static int PromptIterations()
        {
            Console.Write("Enter the number of simulation iterations (e.g., 10): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (int.TryParse(input, out int value) && value > 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a positive integer: ");
            }
        }

        public static double PromptMonthlyWithdrawal()
        {
            Console.Write("Enter the monthly withdrawal amount in today's dollars (e.g., 8000): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }

        public static bool PromptEnableRothConversions()
        {
            Console.Write("Use Roth conversions to fill the 10%/12% brackets? (y/n): ");
            while (true)
            {
                string input = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (input == "y" || input == "yes") return true;
                if (input == "n" || input == "no") return false;
                Console.Write("Invalid input. Please enter y or n: ");
            }
        }

        public static DateOnly PromptBirthdate()
        {
            Console.Write("Enter your birthdate (MM/DD/YYYY): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (DateOnly.TryParse(input, out DateOnly value) && value <= DateOnly.FromDateTime(DateTime.Today))
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a valid past date as MM/DD/YYYY: ");
            }
        }

        public static double PromptInitialTaxableBalance()
        {
            Console.Write("Enter the initial taxable balance (e.g., 1120000): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }

        public static double PromptInitialRothBasis()
        {
            Console.Write("Enter the initial Roth basis - what you contributed, i.e. cost basis (e.g., 15000): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }

        public static double PromptInitialRothUnrealizedGain()
        {
            Console.Write("Enter the initial Roth unrealized gain - current value minus basis (e.g., 5000): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }

        public static double PromptInitialBrokerageBasis()
        {
            Console.Write("Enter the initial Brokerage basis - what you paid in, i.e. cost basis (e.g., 500000): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }

        public static double PromptInitialBrokerageUnrealizedGain()
        {
            Console.Write("Enter the initial Brokerage unrealized gain - current value minus basis (e.g., 200000): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }

        public static double PromptNewMoney()
        {
            Console.Write("Enter the amount of new money to be added (e.g., inheritance) [0 for none]: ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }

        public static int PromptYearNewMoney()
        {
            Console.Write("Enter the year (0-based) when the new money should be added: ");
            while (true)
            {
                string input = Console.ReadLine();
                if (int.TryParse(input, out int value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative integer: ");
            }
        }

        public static DateOnly PromptRetirementDate(DateOnly birthdate)
        {
            var earliest = new DateOnly(FederalTaxBrackets.Year, 1, 1);
            Console.Write($"Enter your retirement date - withdrawals start then, and balances are as of it (MM/DD/YYYY, {earliest.Year} or later): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (DateOnly.TryParse(input, out DateOnly value) && value >= earliest && value > birthdate && value <= birthdate.AddYears(100))
                {
                    return value;
                }
                Console.Write($"Invalid input. Please enter a date from {earliest.Year} on, after your birthdate and before age 100: ");
            }
        }

        public static DateOnly PromptSocialSecurityStartDate(DateOnly birthdate)
        {
            Console.Write("Enter the date of your first Social Security payment (MM/DD/YYYY, between ages 62 and 70): ");
            while (true)
            {
                string input = Console.ReadLine();
                if (DateOnly.TryParse(input, out DateOnly value) && value >= birthdate.AddYears(62) && value <= birthdate.AddYears(70))
                {
                    return value;
                }
                Console.Write($"Invalid input. Please enter a date between {birthdate.AddYears(62):MM/dd/yyyy} and {birthdate.AddYears(70):MM/dd/yyyy}: ");
            }
        }

        public static double PromptSocialSecurityMonthlyAmount()
        {
            Console.Write("Enter the monthly Social Security benefit in today's dollars (e.g., 2500) [0 for none]: ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }

        public static double PromptAnnualStandardDeduction()
        {
            Console.Write("Enter the annual standard deduction (e.g., 16000) [0 for none]: ");
            while (true)
            {
                string input = Console.ReadLine();
                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }
                Console.Write("Invalid input. Please enter a non-negative number: ");
            }
        }
    }


}
