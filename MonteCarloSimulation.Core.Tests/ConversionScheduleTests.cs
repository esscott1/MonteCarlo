namespace MonteCarloSimulation.Core.Tests
{
    // A conversion schedule picks a different ceiling each year (the Strategy Lab's schedule study). The app never
    // passes one, so its runs must be unchanged; and a schedule must behave the same in the full and lean run modes.
    public class ConversionScheduleTests
    {
        private static SimulationParameters Parameters(WithdrawalStrategy strategy) => new()
        {
            Years = 35,
            Iterations = 1,
            Withdrawal = 85_000,
            Birthdate = new DateOnly(1966, 9, 20),
            RetirementDate = new DateOnly(2027, 1, 1),
            InitialTaxableBalance = 1_800_000,
            InitialRothBasis = 60_000,
            InitialRothUnrealizedGain = 40_000,
            InitialBrokerageBasis = 250_000,
            InitialBrokerageUnrealizedGain = 250_000,
            Mean = 0.07,
            StdDev = 0.15,
            SocialSecurityStartDate = new DateOnly(2034, 9, 20),
            SocialSecurityMonthlyAmount = 3_000,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            WithdrawalStrategy = strategy,
            ScenarioDescription = "Conversion schedules"
        };

        // The top of 24% before 63, then the 12% fill
        private static ConversionCeiling? MoreBefore63(RetirementYear year, Accounts _) =>
            year.AgeAtStart < 63 ? ConversionTargets.TopOf24 : RothConversion.DefaultCeiling;

        [Theory]
        [InlineData(WithdrawalStrategy.TaxOptimized)]
        [InlineData(WithdrawalStrategy.ProRata)]
        public void AConstantSchedule_GivesBitIdenticalRuns(WithdrawalStrategy strategyKind)
        {
            var parameters = Parameters(strategyKind);
            var timeline = RetirementTimeline.Build(parameters);
            var strategy = WithdrawalStrategies.For(strategyKind);

            for (int seed = 0; seed < 100; seed++)
            {
                var plain = RunSimulator.Simulate(parameters, timeline, strategy, new Random(seed), ConversionTargets.TopOf22, out _);
                var scheduled = RunSimulator.Simulate(parameters, timeline, strategy, new Random(seed), RothConversion.DefaultCeiling, out _,
                    (_, _) => ConversionTargets.TopOf22);

                Assert.Equal(plain.FailureYear, scheduled.FailureYear);
                Assert.Equal(plain.Years, scheduled.Years);
            }
        }

        [Fact]
        public void TheLeanRun_MatchesTheFullRun_WithASchedule()
        {
            var parameters = Parameters(WithdrawalStrategy.TaxOptimized);
            var timeline = RetirementTimeline.Build(parameters);
            var strategy = WithdrawalStrategies.For(WithdrawalStrategy.TaxOptimized);
            var returns = RunSimulator.SeededReturns(parameters, timeline.Count, 100);

            for (int path = 0; path < returns.Length; path++)
            {
                var full = RunSimulator.Simulate(parameters, timeline, strategy, new Random(path), null, out var fullAccounts, MoreBefore63);
                bool survives = RunSimulator.Survives(parameters, timeline, strategy, returns[path], null, out var leanAccounts, MoreBefore63);

                Assert.Equal(!full.Failed, survives);
                Assert.Equal(fullAccounts.Taxable, leanAccounts.Taxable);
                Assert.Equal(fullAccounts.Roth, leanAccounts.Roth);
                Assert.Equal(fullAccounts.Brokerage, leanAccounts.Brokerage);
            }
        }

        [Fact]
        public void TheScheduleChoosesEachYearsLine_AndNullConvertsNothing()
        {
            var parameters = Parameters(WithdrawalStrategy.TaxOptimized);
            var timeline = RetirementTimeline.Build(parameters);
            var strategy = WithdrawalStrategies.For(WithdrawalStrategy.TaxOptimized);

            // Nothing before 63, the top of 24% from then on
            var run = RunSimulator.Simulate(parameters, timeline, strategy, new Random(7), RothConversion.DefaultCeiling, out _,
                (year, _) => year.AgeAtStart < 63 ? null : ConversionTargets.TopOf24);

            var early = run.Years.Where(y => y.AgeInYear < 63).ToList();
            var late = run.Years.Where(y => y.AgeInYear >= 63 && y.TaxableBalance > 1_000).ToList();
            Assert.NotEmpty(early);
            Assert.All(early, y => Assert.Equal(0, y.RothConversionAmount));
            Assert.Contains(late, y => y.RothConversionAmount > 0);
            // Converting to the top of 24% puts the year's ordinary income in the 24% bracket
            Assert.Contains(late, y => y.RothConversionAmount > 0 && y.OrdinaryBracketRate == 0.24);
        }

        [Fact]
        public void WithConversionsOff_TheScheduleIsIgnored()
        {
            var parameters = Parameters(WithdrawalStrategy.TaxOptimized);
            parameters.EnableRothConversions = false;
            var timeline = RetirementTimeline.Build(parameters);
            var strategy = WithdrawalStrategies.For(WithdrawalStrategy.TaxOptimized);

            var run = RunSimulator.Simulate(parameters, timeline, strategy, new Random(3), RothConversion.DefaultCeiling, out _, MoreBefore63);

            Assert.All(run.Years, y => Assert.Equal(0, y.RothConversionAmount));
        }
    }
}
