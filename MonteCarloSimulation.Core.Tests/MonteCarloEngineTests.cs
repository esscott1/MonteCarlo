namespace MonteCarloSimulation.Core.Tests
{
    public class MonteCarloEngineTests
    {
        [Fact]
        public void Run_AllIterationsSucceed_WhenReturnsExceedWithdrawals()
        {
            var parameters = new SimulationParameters
            {
                Years = 20,
                Iterations = 50,
                Withdrawal = 20_000,
                InitialTaxableBalance = 500_000,
                InitialNontaxableBalance = 500_000,
                Mean = 0.07,
                StdDev = 0, // every run gets exactly Mean as its return every year - fully deterministic
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Deterministic 7% return, small withdrawal"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.Equal(parameters.Iterations, output.Result.EndingBalances.Count);
        }

        [Fact]
        public void Run_AllIterationsFail_WhenWithdrawalExceedsBalance()
        {
            var parameters = new SimulationParameters
            {
                Years = 20,
                Iterations = 50,
                Withdrawal = 500_000,
                InitialTaxableBalance = 300_000,
                InitialNontaxableBalance = 300_000,
                Mean = 0,
                StdDev = 0, // every run gets exactly 0% return every year - fully deterministic
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Deterministic 0% return, withdrawal far exceeds balance"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.Equal(parameters.Iterations, output.Result.OutOfMoneyCount);
        }

        [Fact]
        public void Run_ApproximatelyEightyPercentSucceed_WithVolatileScenario()
        {
            var parameters = new SimulationParameters
            {
                Years = 30,
                Iterations = 150,
                Withdrawal = 45_000,
                InitialTaxableBalance = 700_000,
                InitialNontaxableBalance = 700_000,
                Mean = 0.0807,
                StdDev = 0.1915, // "Last 95 years of S&P" preset volatility
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Volatile scenario calibrated to land near 80% survival"
            };

            var output = MonteCarloEngine.Run(parameters);
            double passRate = 1.0 - (output.Result.OutOfMoneyCount / (double)parameters.Iterations);

            Assert.InRange(passRate, 0.65, 0.95);
        }

        [Fact]
        public void Run_ApproximatelyTwentyPercentSucceed_WithVolatileScenario()
        {
            var parameters = new SimulationParameters
            {
                Years = 30,
                Iterations = 300,
                Withdrawal = 105_000,
                InitialTaxableBalance = 700_000,
                InitialNontaxableBalance = 700_000,
                Mean = 0.0807,
                StdDev = 0.1915, // "Last 95 years of S&P" preset volatility
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Volatile scenario calibrated to land near 20% survival"
            };

            var output = MonteCarloEngine.Run(parameters);
            double passRate = 1.0 - (output.Result.OutOfMoneyCount / (double)parameters.Iterations);

            Assert.InRange(passRate, 0.10, 0.35);
        }
    }
}
