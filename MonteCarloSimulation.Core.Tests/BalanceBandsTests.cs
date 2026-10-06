namespace MonteCarloSimulation.Core.Tests
{
    // The Scenario runner's chart data: the runs' total balance on the retirement date and at the end of each model
    // year, as the 10th percentile, the median and the 90th percentile. A run that ran out counts as $0.
    public class BalanceBandsTests
    {
        private static SimulationParameters Parameters(double withdrawal, double stdDev) => new()
        {
            Years = 30,
            Iterations = 7,
            Withdrawal = withdrawal,
            Birthdate = new DateOnly(1966, 8, 20),
            RetirementDate = new DateOnly(2027, 7, 1),
            InitialTaxableBalance = 700_000,
            InitialRothBasis = 40_000,
            InitialRothUnrealizedGain = 60_000,
            InitialBrokerageBasis = 150_000,
            InitialBrokerageUnrealizedGain = 50_000,
            Mean = 0.05,
            StdDev = stdDev,
            SocialSecurityStartDate = new DateOnly(2033, 8, 20),
            SocialSecurityMonthlyAmount = 2_500,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            ScenarioDescription = "balance bands"
        };

        [Fact]
        public void Percentile_InterpolatesBetweenTheClosestRanks()
        {
            double[] tens = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
            Assert.Equal(19, BalanceBands.Percentile(tens, 0.10), 9);
            Assert.Equal(55, BalanceBands.Percentile(tens, 0.50), 9);
            Assert.Equal(91, BalanceBands.Percentile(tens, 0.90), 9);
            Assert.Equal(42, BalanceBands.Percentile([42], 0.9));
            Assert.Equal(15, BalanceBands.Percentile([10, 20], 0.5));
            Assert.Equal(0, BalanceBands.Percentile([], 0.5));
        }

        [Fact]
        public void IdenticalRuns_GiveOneLine_StartingAtTheStartingBalance_OnTheRetirementDateThenEachYearEnd()
        {
            var parameters = Parameters(60_000, 0);
            var output = MonteCarloEngine.Run(parameters, new Random(1));
            var bands = output.BalanceBands;
            var run = output.Result.Runs[0].Years;

            Assert.False(output.Result.Runs[0].Failed);
            Assert.Equal(run.Count + 1, bands.Count);
            Assert.Equal(new BalanceBandPoint(new DateOnly(2027, 7, 1), 1_000_000, 1_000_000, 1_000_000), bands[0]);
            Assert.Equal(new DateOnly(2028, 1, 1), bands[1].Date);
            Assert.Equal(new DateOnly(2057, 1, 1), bands[^2].Date);
            Assert.Equal(new DateOnly(2057, 7, 1), bands[^1].Date);
            for (int year = 0; year < run.Count; year++)
            {
                Assert.Equal(run[year].Balance, bands[year + 1].Lower);
                Assert.Equal(run[year].Balance, bands[year + 1].Middle);
                Assert.Equal(run[year].Balance, bands[year + 1].Upper);
            }
        }

        [Fact]
        public void RunsThatRanOut_CountAsZero_FromTheirFailureYearOn()
        {
            var output = MonteCarloEngine.Run(Parameters(400_000, 0), new Random(1));
            var failureYear = output.Result.Runs[0].FailureYear!.Value;

            Assert.All(output.Result.Runs, r => Assert.Equal(failureYear, r.FailureYear));
            Assert.Equal(32, output.BalanceBands.Count); // 31 model years (partial first and last), plus the start
            Assert.True(output.BalanceBands[failureYear].Middle > 0);
            Assert.All(output.BalanceBands.Skip(failureYear + 1), p => Assert.Equal((0.0, 0.0, 0.0), (p.Lower, p.Middle, p.Upper)));
        }

        [Fact]
        public void VolatileRuns_KeepTheBandsInOrder()
        {
            var output = MonteCarloEngine.Run(Parameters(40_000, 0.18), new Random(5));
            Assert.All(output.BalanceBands, p => Assert.True(p.Lower <= p.Middle && p.Middle <= p.Upper, p.ToString()));
            Assert.True(output.BalanceBands[^1].Upper > output.BalanceBands[^1].Lower);
        }
    }
}
