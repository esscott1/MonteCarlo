namespace MonteCarloSimulation.Core.Tests
{
    // The Roth conversion ceiling is pluggable (for the Strategy Lab). The app's own path must be unchanged: the
    // default overloads and an explicitly passed OrdinaryFillCeiling give bit-identical runs.
    public class ConversionCeilingTests
    {
        private static SimulationParameters Parameters(WithdrawalStrategy strategy) => new()
        {
            Years = 30,
            Iterations = 1,
            Withdrawal = 90_000,
            Birthdate = new DateOnly(1968, 3, 14),
            RetirementDate = new DateOnly(2027, 7, 1),
            InitialTaxableBalance = 1_200_000,
            InitialRothBasis = 100_000,
            InitialRothUnrealizedGain = 60_000,
            InitialBrokerageBasis = 250_000,
            InitialBrokerageUnrealizedGain = 350_000,
            Mean = 0.0807,
            StdDev = 0.1915,
            NewMoney = 200_000,
            YearNewMoney = 6,
            SocialSecurityStartDate = new DateOnly(2035, 3, 14),
            SocialSecurityMonthlyAmount = 3_100,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            WithdrawalStrategy = strategy,
            ScenarioDescription = "Conversion ceiling equivalence"
        };

        [Theory]
        [InlineData(WithdrawalStrategy.TaxOptimized)]
        [InlineData(WithdrawalStrategy.ProRata)]
        public void ExplicitDefaultCeiling_GivesBitIdenticalRuns(WithdrawalStrategy strategyKind)
        {
            var parameters = Parameters(strategyKind);
            var timeline = RetirementTimeline.Build(parameters);
            var strategy = WithdrawalStrategies.For(strategyKind);
            ConversionCeiling explicitCeiling = (taxYear, ordinary, gains) => taxYear.OrdinaryFillCeiling(ordinary, gains);

            for (int seed = 0; seed < 200; seed++)
            {
                var viaDefault = RunSimulator.Simulate(parameters, timeline, strategy, new Random(seed));
                var viaOverload = RunSimulator.Simulate(parameters, timeline, strategy, new Random(seed), explicitCeiling, out _);
                var viaEngine = MonteCarloEngine.Run(parameters, new Random(seed)).Result.Runs[0];

                Assert.Equal(viaDefault.FailureYear, viaOverload.FailureYear);
                Assert.Equal(viaDefault.Years, viaOverload.Years); // record value equality, field by field
                Assert.Equal(viaDefault.Years, viaEngine.Years);
            }
        }

        [Fact]
        public void ZeroCeiling_ConvertsNothing_LikeConversionsOff()
        {
            var on = Parameters(WithdrawalStrategy.TaxOptimized);
            var off = Parameters(WithdrawalStrategy.TaxOptimized);
            off.EnableRothConversions = false;
            var timeline = RetirementTimeline.Build(on);
            var strategy = WithdrawalStrategies.For(WithdrawalStrategy.TaxOptimized);

            for (int seed = 0; seed < 50; seed++)
            {
                var zero = RunSimulator.Simulate(on, timeline, strategy, new Random(seed), (_, _, _) => 0, out _);
                var none = RunSimulator.Simulate(off, timeline, strategy, new Random(seed));
                Assert.Equal(none.Years, zero.Years);
            }
        }

        [Fact]
        public void HigherCeiling_ConvertsMore_AndFinalAccountsMatchLastYear()
        {
            var parameters = Parameters(WithdrawalStrategy.TaxOptimized);
            parameters.StdDev = 0;
            var timeline = RetirementTimeline.Build(parameters);
            var strategy = WithdrawalStrategies.For(WithdrawalStrategy.TaxOptimized);
            ConversionCeiling top24 = (taxYear, _, _) =>
                taxYear.StandardDeduction + taxYear.Brackets.First(b => b.Rate >= 0.32).LowerBound * taxYear.InflationFactor;

            var standard = RunSimulator.Simulate(parameters, timeline, strategy, new Random(1));
            var higher = RunSimulator.Simulate(parameters, timeline, strategy, new Random(1), top24, out var accounts);

            Assert.True(higher.Years[0].RothConversionAmount > standard.Years[0].RothConversionAmount + 1_000);
            var last = higher.Years[^1];
            Assert.Equal(last.TaxableBalance, accounts.Taxable);
            Assert.Equal(last.BrokerageBalance, accounts.Brokerage);
            Assert.Equal(last.RothBalance, accounts.Roth);
        }
    }
}
