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
            if (result.OutOfMoneyCount > 0)
            {
                Console.WriteLine(output.OutOfMoneyMessage);
                Console.WriteLine("----------------------------------------------------");
                double survival = 1 - (result.OutOfMoneyCount / parameters.Iterations);
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
                Console.WriteLine($"\nInheritance of  {parameters.NewMoney:C0} in  {DateTime.Now.Year + parameters.YearNewMoney} was considered");

                Console.WriteLine($"\nAverage year of failures ran out of money in year {result.YearsOutOfMoney.Average():F0} with an Avg return of {result.FailedScenarioAverages.Average():P4}");
                Console.WriteLine($"\nSee Above for failed scenarios and their rates of return, withdrawals, and balances.\n");
            }
            else //all scenarios succeeded
            {
                Console.WriteLine();
                Console.WriteLine($"\nlast run balances: ");
                if (output.LastBalances != null && output.LastAnnualReturns != null && output.LastAnnualWithdrawals != null
                    && output.LastTaxableBalances != null && output.LastNontaxableBalances != null
                    && output.LastTaxableWithdrawals != null && output.LastNontaxableWithdrawals != null)
                {
                    for (int ji = 1; ji < output.LastBalances.Count; ji++)
                    {
                        Console.WriteLine(
                            $"Year {ji}\n withdrawals: {output.LastAnnualWithdrawals[ji]:C0}, (" +
                            $"taxable: {output.LastTaxableWithdrawals[ji]:C0}, " +
                            $"nontaxable: {output.LastNontaxableWithdrawals[ji]:C0})\n " +
                            $"years return ($): {output.LastAnnualReturns[ji]:C0}\n " +
                            $"total balance: {output.LastBalances[ji]:C0} (" +
                            $"taxable balance: {output.LastTaxableBalances[ji]:C0}, " +
                            $"nontaxable balance: {output.LastNontaxableBalances[ji]:C0}), ");
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
            for (int r = 0; r < result.EndingBalances.Count; r++)
            {
                string failureNote = result.FailureYears[r].HasValue
                    ? $", ran out of money in year {result.FailureYears[r]}"
                    : "";
                Console.WriteLine(
                    $"Run {r + 1}: Ending balance: {result.EndingBalances[r]:C0}, " +
                    $"Average annual return: {result.AverageAnnualReturns[r]:P2}, " +
                    $"Highest return: year {result.HighestReturnYears[r]} ({result.HighestReturnValues[r]:P2}), " +
                    $"Lowest return: year {result.LowestReturnYears[r]} ({result.LowestReturnValues[r]:P2}){failureNote}");
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

            return new SimulationParameters
            {
                Years = PromptYears(),
                Iterations = PromptIterations(),
                Withdrawal = PromptWithdrawal(),
                InitialInvestment = PromptInitialInvestment(),
                Mean = scenario.Mean,
                StdDev = scenario.StdDev,
                NewMoney = PromptNewMoney(),
                YearNewMoney = PromptYearNewMoney(),
                SocialSecurityYearsUntilStart = PromptSocialSecurityYearsUntilStart(),
                SocialSecurityAnnualAmount = PromptSocialSecurityAnnualAmount(),
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

        public static double PromptWithdrawal()
        {
            Console.Write("Enter the annual withdrawal amount (e.g., 120000): ");
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

        public static double PromptInitialInvestment()
        {
            Console.Write("Enter the initial investment amount (e.g., 2800000): ");
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

        public static int PromptSocialSecurityYearsUntilStart()
        {
            Console.Write("Enter the number of years until Social Security income begins: ");
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

        public static double PromptSocialSecurityAnnualAmount()
        {
            Console.Write("Enter the initial annual Social Security amount (e.g., 50000) [0 for none]: ");
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
