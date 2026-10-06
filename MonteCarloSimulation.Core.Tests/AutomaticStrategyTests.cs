namespace MonteCarloSimulation.Core.Tests
{
    // The app's Automatic choices: the engine tries every (withdrawal order, conversion target) pair on the user's
    // inputs and runs the one that survives more seeded paths, ties going to more after-tax money left.
    public class AutomaticStrategyTests
    {
        private static SimulationParameters Parameters(double withdrawal) => new()
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
            ScenarioDescription = "automatic strategy"
        };

        [Fact]
        public void TheAppDefaults_AreAutomatic()
        {
            var defaults = new SimulationParameters { ScenarioDescription = "" };
            Assert.Equal(WithdrawalStrategy.Automatic, defaults.WithdrawalStrategy);
            Assert.Equal(RothConversionTarget.Automatic, defaults.RothConversionTarget);
        }

        [Fact]
        public void Candidates_ExpandAutomatic_AndOnlyNoneWithConversionsOff()
        {
            var parameters = Parameters(60_000);
            Assert.Equal(8, AutomaticStrategy.Candidates(parameters).Count);

            parameters.EnableRothConversions = false;
            Assert.All(AutomaticStrategy.Candidates(parameters), c => Assert.Equal(RothConversionTarget.None, c.Target));
            Assert.Equal(2, AutomaticStrategy.Candidates(parameters).Count);

            parameters.EnableRothConversions = true;
            parameters.WithdrawalStrategy = WithdrawalStrategy.ProRata;
            parameters.RothConversionTarget = RothConversionTarget.Bracket22;
            Assert.Equal([(WithdrawalStrategy.ProRata, RothConversionTarget.Bracket22)], AutomaticStrategy.Candidates(parameters));
        }

        [Theory]
        [InlineData(45_000)]
        [InlineData(60_000)]
        [InlineData(75_000)]
        public void Resolve_PicksThePairThatSurvivesMorePaths_ThenLeavesMoreAfterTax(double withdrawal)
        {
            var parameters = Parameters(withdrawal);
            var timeline = RetirementTimeline.Build(parameters);

            var chosen = AutomaticStrategy.Resolve(parameters, timeline);

            var chosenScore = AutomaticStrategy.Score(parameters, timeline, chosen.Order, chosen.Target);
            foreach (var other in AutomaticStrategy.Candidates(parameters).Where(c => c != chosen))
            {
                var otherScore = AutomaticStrategy.Score(parameters, timeline, other.Order, other.Target);
                Assert.True(chosenScore.Survivors > otherScore.Survivors
                    || (chosenScore.Survivors == otherScore.Survivors && chosenScore.AfterTaxLeft >= otherScore.AfterTaxLeft));
            }
        }

        [Fact]
        public void Engine_RunsAndReportsTheChosenPair_ExactlyAsIfItWereRequested()
        {
            var automatic = Parameters(60_000);
            var output = MonteCarloEngine.Run(automatic, new Random(42));

            Assert.NotEqual(WithdrawalStrategy.Automatic, output.WithdrawalStrategy);
            Assert.NotEqual(RothConversionTarget.Automatic, output.RothConversionTarget);
            var explicitPair = Parameters(60_000);
            explicitPair.WithdrawalStrategy = output.WithdrawalStrategy;
            explicitPair.RothConversionTarget = output.RothConversionTarget;
            var same = MonteCarloEngine.Run(explicitPair, new Random(42));
            Assert.Equal(output.RothConversionTarget, same.RothConversionTarget);
            for (int i = 0; i < same.Result.Runs.Count; i++)
                Assert.Equal(same.Result.Runs[i].Years, output.Result.Runs[i].Years);
        }

        [Fact]
        public void ExplicitChoices_PassThrough_AndAutomaticNeverReachesTheYearLoop()
        {
            var parameters = Parameters(60_000);
            parameters.WithdrawalStrategy = WithdrawalStrategy.ProRata;
            parameters.RothConversionTarget = RothConversionTarget.Bracket24;
            var output = MonteCarloEngine.Run(parameters, new Random(1));
            Assert.Equal(WithdrawalStrategy.ProRata, output.WithdrawalStrategy);
            Assert.Equal(RothConversionTarget.Bracket24, output.RothConversionTarget);
            Assert.Throws<InvalidOperationException>(() => WithdrawalStrategies.For(WithdrawalStrategy.Automatic));
            Assert.Throws<InvalidOperationException>(() => ConversionTargets.CeilingFor(RothConversionTarget.Automatic));
        }

        [Fact]
        public void ConversionsOff_MeansNoneWhateverTheTarget()
        {
            var parameters = Parameters(60_000);
            parameters.EnableRothConversions = false;
            var output = MonteCarloEngine.Run(parameters, new Random(1));
            Assert.Equal(RothConversionTarget.None, output.RothConversionTarget);
            Assert.All(output.Result.Runs.SelectMany(r => r.Years), y => Assert.Equal(0, y.RothConversionAmount));
        }

        [Theory]
        [InlineData(RothConversionTarget.Bracket12, 66_400)]
        [InlineData(RothConversionTarget.Bracket22, 121_700)]
        [InlineData(RothConversionTarget.Bracket24, 217_775)]
        public void TargetCeilings_AreThe2026BracketLines(RothConversionTarget target, double expected)
        {
            var taxYear = new TaxYear(16_000, 1.0, FederalTaxBrackets.Single2026, FederalTaxBrackets.CapitalGainsSingle2026);
            Assert.Equal(expected, ConversionTargets.CeilingFor(target)!(taxYear, 0, 0), 6);
            Assert.Null(ConversionTargets.CeilingFor(RothConversionTarget.None));
        }
    }
}
