using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace MonteCarloSimulation1
{


    public class MonteCarloSimulation
    {
        // last 95 years of S&P
        //private const string inputs = "based on last 95 years of S&P";
        //private const double Mean = .0807;
        //private const double StandardDeviation = .1915;

        // last 30 years of S&P
        private const string inputs = "based on last 30 years of S&P";
        private const double Mean = .1007;
        private const double StandardDeviation = .1688;
        /// <summary>
        /// 10 year bond avg since 1962 5.83% std dev 2.953%:  
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {
            int years = 50;
            int months = 360;
            int iterations = 10000;
            
            double withdrawl = 120000;
            double mean = Mean;
            double standardDeviation = StandardDeviation;
            double initialInvestment = 2800000.0;
            Random random = new Random();
            double outOfMoneyCount = 0;
            StringBuilder outOfMoneyMessage = new StringBuilder();
            double newMoney = 500000;
            int yearNewMoney = 10;
            List<double> allRates = new List<double>(); 
            List<int> yearsOutofMoney = new List<int>();


            Console.WriteLine("Monte Carlo Simulation Results:");


            for (int i = 0; i < iterations; i++)
            {
                double currentInvestment = initialInvestment; // Start with the initial investment
                double iwithdrawl = withdrawl;
                double periodwithdrawl = 0;
                List<double> rates = new List<double>();
                List<double> withdrawals = new List<double>();  
                List<double> bal = new List<double>();
                Random eRand = new Random();
                double inflation = 0.025;
                double ss = 0;

              //  Console.WriteLine($"\nIteration {i + 1}:");

                for (int run = 0; run < years; run++) // Perform 50 runs which is 50 years.
                {
                    if(run>9)
                    { ss = 50000; }
                    if(run>19)
                    { inflation = .01; }
                    // Generate a random number with a normal distribution using the Box-Muller transform
                    double interestRate = GetRateBoxMullerTransform(mean, standardDeviation, random);
                   // double interestRate = CalculateInverseCDF(eRand.NextDouble());
                    rates.Add(interestRate);  //for displaying rates
                    allRates.Add(interestRate); // for displaying all rates

                    iwithdrawl = iwithdrawl * (1 + inflation);
                    periodwithdrawl = iwithdrawl - ss;
                    withdrawals.Add(periodwithdrawl); // for displaying withdrawals
                    // Calculate the ending balance

                    double endingBalance = currentInvestment * (1 + interestRate) - periodwithdrawl;
                    if(run == yearNewMoney)
                        endingBalance = endingBalance + newMoney; // Add new money at the end of yearNewMoney
                    bal.Add(endingBalance);
                   // Console.WriteLine($"  Year {run + 1}: Begin Bal = {currentInvestment:C0} Interest Rate = {interestRate:P2}, Withdrawl = {iwithdrawl:C0} End Bal = {endingBalance:C0}");
                    if (endingBalance < 0)
                    {
                        yearsOutofMoney.Add(run);
                        outOfMoneyCount++;
                        outOfMoneyMessage.Append($"\nIteration {i + 1}: in Year {run}:  Average rate of return {rates.Average():P2}");
                        for (int c = 0; c < rates.Count; c++)
                        {
                            outOfMoneyMessage.Append($"\nYear {c} rate: {rates[c]:P2} with draw: {withdrawals[c]:C0} with bal: {bal[c]:C0}");
                        }
                        outOfMoneyMessage.Append('\n');
                        break;
                    }
                    currentInvestment = endingBalance; // Use ending balance as initial for next run
                }
                // initialInvestment = currentInvestment;
            }
            if (outOfMoneyCount > 0)
            {
                Console.WriteLine();
                Console.WriteLine(outOfMoneyMessage.ToString()); 
                double survial = outOfMoneyCount / iterations;
                double totalAvgRates = allRates.Average();
                double variance = allRates.Average(n => Math.Pow(n - totalAvgRates, 2));
                double stdDev = Math.Sqrt(variance);
                Console.WriteLine($"\ntotal avg return {totalAvgRates:P4} with std dev {stdDev} based on {inputs}");
                Console.WriteLine($"\nInheritance of  {newMoney:C0} in  {DateTime.Now.Year + yearNewMoney}");
                Console.WriteLine($"\n{outOfMoneyCount} portfolios did not survive {years} years given {iterations} iterations. survival rate: {1-survial:P4}");
                Console.WriteLine($"\n average year failures ran out of money {yearsOutofMoney.Average():F0}");
                Console.WriteLine($"/n{outOfMoneyMessage.Length}");

            }
            Console.WriteLine("\nSimulation complete.");
        }

        private static double GetRateBoxMullerTransform(double mean, double standardDeviation, Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

            // Transform to get interest rate
            double interestRate = mean + standardDeviation * randStdNormal;
            return interestRate;
        }

        // Function to calculate the inverse CDF of the normal distribution.
        // This is an approximation, as there is no closed-form solution.
        private static double CalculateInverseCDF(double p)
        {
            // Approximation for the inverse CDF.
            // Source: https://en.wikipedia.org/wiki/Normal_distribution#Quantile_function
            double a1 = -39.69683028665376;
            double a2 = 220.9460984245205;
            double a3 = -2759.979103979004;
            double a4 = 13835.77518672690;
            double a5 = -30664.14590005163;
            double a6 = 25066.32774311881;

            double b1 = -54.47609879822406;
            double b2 = 161.5858368580461;
            double b3 = -1556.989794985913;
            double b4 = 1221.238011352355;
            double b5 = -288.8167363461239;

            double c1 = -0.3239984597581123;
            double c2 = -0.02100035308924411;
            double c3 = 0.003438151596945617;
            double c4 = -0.0002043224016355399;

            double d1 = -0.9621174000094653;
            double d2 = 0.4374664141464968;
            double d3 = -0.2916749416344412;
            double d4 = 0.04278945251007351;

            double x;

            if (p <= 0.02275)
            {
                x = Math.Sqrt(-2.0 * Math.Log(p));
                x = a1 + x * (a2 + x * (a3 + x * (a4 + x * (a5 + x * a6))));
                x = x / (1.0 + x * (b1 + x * (b2 + x * (b3 + x * (b4 + x * b5)))));
            }
            else if (p >= 0.97725)
            {
                x = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
                x = c1 + x * (c2 + x * (c3 + x * c4));
                x = x / (1.0 + x * (d1 + x * (d2 + x * (d3 + x * d4))));
            }
            else
            {
                x = p - 0.5;
                x = x * x * x;
                x = c1 + x * (c2 + x * (c3 + x * c4));
                x = p - 0.5;
                x = x * x * x;
                x = x / (1.0 + x * (d1 + x * (d2 + x * (d3 + x * d4))));
                x = x / (1.0 + x * (d1 + x * (d2 + x * (d3 + x * d4))));
            }

            return Mean + StandardDeviation * x;
        }
    

    }
}