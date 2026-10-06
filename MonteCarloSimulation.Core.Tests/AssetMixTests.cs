namespace MonteCarloSimulation.Core.Tests
{
    // The Scenario runner's stocks/bonds/cash market: every account earns one blended return a year, drawn from three
    // asset classes with stocks and bonds correlated. Without a mix, the engine draws from Mean/StdDev as before.
    public class AssetMixTests
    {
        private static AssetMix Defaults() => new()
        {
            StockWeight = 0.6, BondWeight = 0.3, CashWeight = 0.1,
            StockMean = 0.08, StockStdDev = 0.19,
            BondMean = 0.045, BondStdDev = 0.04,
            CashMean = 0.035, CashStdDev = 0.01,
            StockBondCorrelation = 0.1
        };

        private static SimulationParameters Parameters(AssetMix? mix, double mean, double stdDev) => new()
        {
            Years = 30,
            Iterations = 20,
            Withdrawal = 70_000,
            Birthdate = new DateOnly(1968, 4, 1),
            RetirementDate = new DateOnly(2027, 1, 1),
            InitialTaxableBalance = 900_000,
            InitialRothBasis = 50_000,
            InitialRothUnrealizedGain = 30_000,
            InitialBrokerageBasis = 200_000,
            InitialBrokerageUnrealizedGain = 150_000,
            Mean = mean,
            StdDev = stdDev,
            AssetMix = mix,
            SocialSecurityStartDate = new DateOnly(2035, 4, 1),
            SocialSecurityMonthlyAmount = 2_600,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            ScenarioDescription = "asset mix"
        };

        [Fact]
        public void TheDefaults_BlendTo6Point5Percent_WithTheCorrelatedStdDev()
        {
            var mix = Defaults();
            Assert.Equal(0.065, mix.ExpectedReturn, 12);
            // (0.6 x 19%)^2 + (0.3 x 4%)^2 + (0.1 x 1%)^2 + 2 x 0.1 x (0.6 x 19%) x (0.3 x 4%)
            Assert.Equal(Math.Sqrt(0.0134146), mix.StdDev, 12);
        }

        [Fact]
        public void WithNoVolatility_ARunMatchesTheSingleReturnRunAtTheBlendedMean()
        {
            var mix = Defaults() with { StockStdDev = 0, BondStdDev = 0, CashStdDev = 0 };
            var mixed = MonteCarloEngine.Run(Parameters(mix, 0, 0), new Random(3));
            var single = MonteCarloEngine.Run(Parameters(null, mix.ExpectedReturn, 0), new Random(3));

            Assert.Equal(single.WithdrawalStrategy, mixed.WithdrawalStrategy);
            Assert.Equal(single.RothConversionTarget, mixed.RothConversionTarget);
            for (int run = 0; run < single.Result.Runs.Count; run++)
            {
                var expected = single.Result.Runs[run].Years;
                var actual = mixed.Result.Runs[run].Years;
                Assert.Equal(expected.Count, actual.Count);
                for (int year = 0; year < expected.Count; year++)
                {
                    Assert.Equal(expected[year].RateOfReturn, actual[year].RateOfReturn);
                    Assert.Equal(expected[year].Balance, actual[year].Balance);
                }
            }
        }

        [Fact]
        public void PerfectlyOppositeStocksAndBonds_CancelOut()
        {
            var mix = new AssetMix
            {
                StockWeight = 0.5, BondWeight = 0.5,
                StockMean = 0.06, StockStdDev = 0.1,
                BondMean = 0.06, BondStdDev = 0.1,
                StockBondCorrelation = -1
            };
            var random = new Random(9);
            for (int i = 0; i < 1_000; i++)
                Assert.Equal(0.06, mix.DrawReturn(random), 12);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.8)]
        [InlineData(-0.5)]
        public void Draws_HaveTheMixesMeanAndStdDev(double correlation)
        {
            // Volatile bonds, so the correlation visibly changes the blended spread
            var mix = new AssetMix
            {
                StockWeight = 0.5, BondWeight = 0.3, CashWeight = 0.2,
                StockMean = 0.08, StockStdDev = 0.2,
                BondMean = 0.04, BondStdDev = 0.1,
                CashMean = 0.03, CashStdDev = 0.02,
                StockBondCorrelation = correlation
            };
            var random = new Random(42);
            const int draws = 200_000;
            double sum = 0, sumOfSquares = 0;
            for (int i = 0; i < draws; i++)
            {
                double r = mix.DrawReturn(random);
                sum += r;
                sumOfSquares += r * r;
            }
            double mean = sum / draws;
            double stdDev = Math.Sqrt(sumOfSquares / draws - mean * mean);

            Assert.Equal(mix.ExpectedReturn, mean, 0.002);
            Assert.Equal(mix.StdDev, stdDev, 0.002);
        }

        [Fact]
        public void WithoutAMix_SeededReturnsAreTheSingleReturnDraws()
        {
            var parameters = Parameters(null, 0.0807, 0.1915);
            Assert.Equal(RunSimulator.SeededReturns(0.0807, 0.1915, 30, 40), RunSimulator.SeededReturns(parameters, 30, 40));
        }

        // The Optimizer draws a single path on demand; it must be that path of the full set, with or without a mix
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SeededReturns_ForSomePaths_MatchThoseOfTheFullSet(bool mix)
        {
            var parameters = Parameters(mix ? Defaults() : null, 0.0807, 0.1915);
            var all = RunSimulator.SeededReturns(parameters, 30, 25);

            Assert.Equal(all[17], RunSimulator.SeededReturns(parameters, 30, 17, 1)[0]);
            Assert.Equal(all[5..9], RunSimulator.SeededReturns(parameters, 30, 5, 4));
        }
    }
}
