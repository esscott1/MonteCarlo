using System;
using System.Text;

namespace MonteCarloSimulation1
{


    public class MonteCarloSimulation
    {
        public static void Main(string[] args)
        {
            int iterations = 100;
            double withdrawl = 120000;
            double mean = 0.12;
            double standardDeviation = 0.15;
            double initialInvestment = 2500000.0;
            Random random = new Random();
            int outOfMoneyCount = 0;
            StringBuilder outOfMoneyMessage = new StringBuilder();  

            Console.WriteLine("Monte Carlo Simulation Results:");

            for (int i = 0; i < iterations; i++)
            {
                double currentInvestment = initialInvestment; // Start with the initial investment

                Console.WriteLine($"\nIteration {i + 1}:");

                for (int run = 0; run < 50; run++) // Perform 50 runs which is 50 years.
                {
                    // Generate a random number with a normal distribution using the Box-Muller transform
                    double u1 = 1.0 - random.NextDouble();
                    double u2 = 1.0 - random.NextDouble();
                    double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

                    // Transform to get interest rate
                    double interestRate = mean + standardDeviation * randStdNormal;

                    // Calculate the ending balance
                    double endingBalance = currentInvestment * (1 + interestRate) - withdrawl;

                    Console.WriteLine($"  Year {run + 1}: Beginning Balance = {currentInvestment:C2} Interest Rate = {interestRate:P2}, Ending Balance = {endingBalance:C2}");
                    if(endingBalance < 0)
                    {
                        outOfMoneyCount++;
                        outOfMoneyMessage.Append($"\nIteration {i + 1}: in Year {run}");
                        break;
                    }
                    currentInvestment = endingBalance; // Use ending balance as initial for next run
                }
               // initialInvestment = currentInvestment;
            }
            if (outOfMoneyCount > 0) { 
                Console.WriteLine(outOfMoneyMessage.ToString());
                Console.WriteLine($"\n{outOfMoneyCount} portfolios did not survive out of {iterations}");
            }
            Console.WriteLine("\nSimulation complete.");
        }
    }
}