namespace MonteCarloSimulation.Core.Tests
{
    // WithdrawalStrategy.Automatic, the app's default: the engine tries each order on the user's inputs and runs the
    // one that survives more seeded paths (ties to more money left at the end).
    public class AutomaticWithdrawalTests
    {
        private static SimulationParameters Parameters(double withdrawal, WithdrawalStrategy order = WithdrawalStrategy.Automatic) => new()
        {
            Years = 30,
            Iterations = 10,
            Withdrawal = withdrawal,
            Birthdate = new DateOnly(1971, 5, 1),
            RetirementDate = new DateOnly(2027, 3, 1),
            InitialTaxableBalance = 1_100_000,
            InitialRothBasis = 30_000,
            InitialRothUnrealizedGain = 20_000,
            InitialBrokerageBasis = 40_000,
            InitialBrokerageUnrealizedGain = 60_000,
            Mean = 0.0807,
            StdDev = 0.1915,
            SocialSecurityStartDate = new DateOnly(2035, 5, 1),
            SocialSecurityMonthlyAmount = 2_400,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            WithdrawalStrategy = order,
            ScenarioDescription = "automatic order"
        };

        [Fact]
        public void TheAppDefault_IsAutomatic()
        {
            Assert.Equal(WithdrawalStrategy.Automatic, new SimulationParameters { ScenarioDescription = "" }.WithdrawalStrategy);
        }

        [Theory]
        [InlineData(45_000)]
        [InlineData(60_000)]
        [InlineData(75_000)]
        public void Resolve_PicksTheOrderThatSurvivesMorePaths_ThenLeavesMore(double withdrawal)
        {
            var parameters = Parameters(withdrawal);
            var timeline = RetirementTimeline.Build(parameters);

            var chosen = AutomaticWithdrawal.Resolve(parameters, timeline);

            var chosenScore = AutomaticWithdrawal.Score(parameters, timeline, chosen);
            foreach (var other in AutomaticWithdrawal.Candidates.Where(c => c != chosen))
            {
                var otherScore = AutomaticWithdrawal.Score(parameters, timeline, other);
                Assert.True(chosenScore.Survivors > otherScore.Survivors
                    || (chosenScore.Survivors == otherScore.Survivors && chosenScore.EndingMoney >= otherScore.EndingMoney));
            }
        }

        [Fact]
        public void Engine_RunsAndReportsTheChosenOrder_ExactlyAsIfItWereRequested()
        {
            var automatic = Parameters(60_000);
            var output = MonteCarloEngine.Run(automatic, new Random(42));

            Assert.NotEqual(WithdrawalStrategy.Automatic, output.WithdrawalStrategy);
            var explicitOrder = Parameters(60_000, output.WithdrawalStrategy);
            var same = MonteCarloEngine.Run(explicitOrder, new Random(42));
            Assert.Equal(same.WithdrawalStrategy, output.WithdrawalStrategy);
            for (int i = 0; i < same.Result.Runs.Count; i++)
                Assert.Equal(same.Result.Runs[i].Years, output.Result.Runs[i].Years);
        }

        [Fact]
        public void AnExplicitOrder_PassesThrough_AndAutomaticNeverReachesTheYearLoop()
        {
            var parameters = Parameters(60_000, WithdrawalStrategy.ProRata);
            Assert.Equal(WithdrawalStrategy.ProRata, AutomaticWithdrawal.Resolve(parameters, RetirementTimeline.Build(parameters)));
            Assert.Equal(WithdrawalStrategy.ProRata, MonteCarloEngine.Run(parameters, new Random(1)).WithdrawalStrategy);
            Assert.Throws<InvalidOperationException>(() => WithdrawalStrategies.For(WithdrawalStrategy.Automatic));
        }
    }
}
