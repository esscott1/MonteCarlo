using System.Text;

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

                var parameters = SimulationParameters.PromptUser();

                // Prepare result object
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

                SimulationReporter.PrintResults(parameters.ScenarioDescription,
                                result,
                                parameters,
                                allRates,
                                outOfMoneyMessage,
                                lastBalances,
                                lastAnnualReturns,
                                lastAnnualWithdrawals,
                                lastTaxableBalances,
                                lastNontaxableBalances,
                                lastTaxableWithdrawals,
                                lastNontaxableWithdrawals
                            );
            }
        }


        private static double GetRateBoxMullerTransform(double mean, double standardDeviation, Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + standardDeviation * randStdNormal;
        }

    }
    public class SimulationParameters
    {
        public int Years { get; set; }
        public int Iterations { get; set; }
        public double Withdrawal { get; set; }
        public double InitialInvestment { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double NewMoney { get; set; }
        public int YearNewMoney { get; set; }
        public int SocialSecurityYearsUntilStart { get; set; }
        public double SocialSecurityAnnualAmount { get; set; }
        public required string ScenarioDescription { get; set; }



        // Factory method to prompt user for all parameters
        public static SimulationParameters PromptUser()
        {
            int option = SimulationPrompt.PromptInvestmentOption();
            string scenarioDescription;
            double mean, standardDeviation;

            switch (option)
            {
                case 1:
                    scenarioDescription = "Use the last 54 years of 10 year govt bonds";
                    mean = 0.0583;
                    standardDeviation = 0.0295;
                    break;
                case 2:
                    scenarioDescription = "Use the last 95 years of S&P returns";
                    mean = 0.0807;
                    standardDeviation = 0.1915;
                    break;
                case 3:
                    scenarioDescription = "Use the last 30 years of S&P returns";
                    mean = 0.1007;
                    standardDeviation = 0.1688;
                    break;
                case 4:
                    scenarioDescription = "Use the current 10 year bond yield";
                    mean = 0.0443;
                    standardDeviation = 0.001;
                    break;
                default:
                    scenarioDescription = "Use the current 10 year bond yield";
                    mean = 0.0443;
                    standardDeviation = 0.001;
                    break;
            }

            return new SimulationParameters
            {
                Years = SimulationPrompt.PromptYears(),
                Iterations = SimulationPrompt.PromptIterations(),
                Withdrawal = SimulationPrompt.PromptWithdrawal(),
                InitialInvestment = SimulationPrompt.PromptInitialInvestment(), 
                Mean = mean,
                StdDev = standardDeviation,
                NewMoney = SimulationPrompt.PromptNewMoney(),
                YearNewMoney = SimulationPrompt.PromptYearNewMoney(),
                SocialSecurityYearsUntilStart = SimulationPrompt.PromptSocialSecurityYearsUntilStart(),
                SocialSecurityAnnualAmount = SimulationPrompt.PromptSocialSecurityAnnualAmount(),
                ScenarioDescription = scenarioDescription
            };
        }
    }

    public class SimulationResult
    {
        public double OutOfMoneyCount { get; set; }
        public List<int> YearsOutOfMoney { get; set; }
        public List<double> FailedScenarioAverages { get; set; }
        public List<double> SuccessMoneyRemaining { get; set; }
        public List<double> EndingBalances { get; set; }
        public List<double> AverageAnnualReturns { get; set; }
        public List<int?> FailureYears { get; set; }
        public List<int> HighestReturnYears { get; set; }
        public List<double> HighestReturnValues { get; set; }
        public List<int> LowestReturnYears { get; set; }
        public List<double> LowestReturnValues { get; set; }
        // Add other result fields as needed
    }
    public static class SimulationReporter
    {
        public static void PrintResults(
            String scenarioDescription,
            SimulationResult result,
            SimulationParameters parameters,
            List<double> allRates,
            StringBuilder outOfMoneyMessage,
            List<double> lastBalances,
            List<double> lastAnnualReturns,
            List<double> lastAnnualWithdrawals,
            List<double> lastTaxableBalances,
            List<double> lastNontaxableBalances,
            List<double> lastTaxableWithdrawals,
            List<double> lastNontaxableWithdrawals)

        // ---- OUTPUT SECTION: Print after all iterations ----
        { 
            if (result.OutOfMoneyCount > 0)
            {
                Console.WriteLine(outOfMoneyMessage.ToString());
                Console.WriteLine("----------------------------------------------------");
                double survival = 1 - (result.OutOfMoneyCount / parameters.Iterations);
                if (survival > 0.8)
                    Smile();
                else
                    Frown();
                Console.WriteLine($"\n{result.OutOfMoneyCount} portfolios did not survive {parameters.Years} years given {parameters.Iterations} iterations. survival rate: {survival:P4}");
                Console.WriteLine($"\nThe Scenario: {scenarioDescription} with Initial mean: {parameters.Mean:P4}  Initial standard deviation: {parameters.StdDev:P4}");

                double totalAvgRates = allRates.Average();
                double variance = allRates.Average(n => Math.Pow(n - totalAvgRates, 2));
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
                if (lastBalances != null && lastAnnualReturns != null && lastAnnualWithdrawals != null)
                {
                    for (int ji = 1; ji < lastBalances.Count; ji++)
                    {
                        Console.WriteLine(
                            $"Year {ji}\n withdrawals: {lastAnnualWithdrawals[ji]:C0}, (" +
                            $"taxable: {lastTaxableWithdrawals[ji]:C0}, " +
                            $"nontaxable: {lastNontaxableWithdrawals[ji]:C0})\n " +
                            $"years return ($): {lastAnnualReturns[ji]:C0}\n " +
                            $"total balance: {lastBalances[ji]:C0} (" +
                            $"taxable balance: {lastTaxableBalances[ji]:C0}, " +
                            $"nontaxable balance: {lastNontaxableBalances[ji]:C0}), ");
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
        public static int PromptInvestmentOption()
        {
            Console.WriteLine("Select an investment scenario:");
            Console.WriteLine("1. 10 year gov bonds - 5.8% return w/std. 2.95%");
            Console.WriteLine("2. Last 95 years of S&P - 8.07% return w/ std. 19.15%");
            Console.WriteLine("3. Last 30 years of S&P - 10.07% return w/ std 16.8%");
            Console.WriteLine("4. Current 10 year bond yield - 4.43% return w/ std 0.1%");
            Console.Write("Enter the number of your choice (1-4): ");

            while (true)
            {
                string input = Console.ReadLine();
                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= 4)
                {
                    return choice;
                }
                Console.Write("Invalid input. Please enter a number between 1 and 4: ");
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
