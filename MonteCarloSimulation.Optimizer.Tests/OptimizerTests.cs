using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer.Tests
{
    public class OptimizerTests
    {
        private static readonly InvestmentScenario Volatile = InvestmentScenarios.ById(2)!; // 95 years of S&P
        private static readonly InvestmentScenario Flat = new(99, "Flat 4%, no volatility", 0.04, 0, "flat");

        private static SimulationParameters Template(int years = 30) => new()
        {
            Years = years,
            Birthdate = new DateOnly(1966, 1, 1),
            RetirementDate = new DateOnly(2026, 1, 1),
            InitialTaxableBalance = 900_000,
            InitialRothBasis = 50_000,
            InitialRothUnrealizedGain = 50_000,
            InitialBrokerageBasis = 200_000,
            InitialBrokerageUnrealizedGain = 200_000,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
            ScenarioDescription = "optimizer test"
        };

        private static readonly SocialSecurityCurve Defaults = new(2_750, 3_900, 4_800);

        // --- Social Security curve ---

        [Fact]
        public void Curve_PassesThroughTheThreeEnteredAmounts()
        {
            Assert.Equal(2_750, Defaults.MonthlyBenefitAtAge(62), 9);
            Assert.Equal(3_900, Defaults.MonthlyBenefitAtAge(67), 9);
            Assert.Equal(4_800, Defaults.MonthlyBenefitAtAge(70), 9);
        }

        [Fact]
        public void Curve_FollowsSsaShapeBetweenTheAnchors()
        {
            // 64 is 36 months early: 20 of the 30 reduction points.
            Assert.Equal(3_900 - 1_150 * 20.0 / 30, Defaults.MonthlyBenefitAtAge(64), 9);
            // 68 is a third of the way through the linear delayed credits.
            Assert.Equal(3_900 + 900.0 / 3, Defaults.MonthlyBenefitAtAge(68), 9);

            var byAge = Enumerable.Range(62, 9).Select(Defaults.MonthlyBenefitAtAge).ToList();
            Assert.Equal(byAge.OrderBy(b => b), byAge); // monotone when the anchors are
        }

        // --- Reading survival levels off break-evens ---

        [Fact]
        public void SpendAtSurvival_ReadsThePercentileOfBreakEvens()
        {
            double[] breakEvens = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();

            Assert.Equal(16, SpendingOptimizer.SpendAtSurvival(breakEvens, 0.85));   // 85 of 100 paths have >= 16
            Assert.Equal(18, SpendingOptimizer.SpendAtSurvival(breakEvens, 0.825));  // 83 paths have >= 18
            Assert.Equal(21, SpendingOptimizer.SpendAtSurvival(breakEvens, 0.80));   // 80 paths have >= 21
        }

        // --- Break-even search ---

        [Fact]
        public void BreakEven_SurvivesAtTheAnswer_AndFailsOneStepAbove()
        {
            var simulator = new PathSimulator(Template(), Flat, new DateOnly(2033, 1, 1), 3_900);

            double breakEven = simulator.BreakEven(0);

            Assert.True(breakEven > 0);
            Assert.True(simulator.Survives(breakEven, 0));
            Assert.False(simulator.Survives(breakEven + PathSimulator.Precision, 0));
        }

        [Theory]
        [InlineData(40_000, 0)]
        [InlineData(80_000, 1)]
        [InlineData(120_000, 2)]
        [InlineData(95_000, 7)]
        public void Survives_MatchesCoresEngineForTheSameSeededPath(double withdrawal, int path)
        {
            var simulator = new PathSimulator(Template(), Volatile, new DateOnly(2033, 1, 1), 3_900);

            Assert.Equal(simulator.SurvivesViaEngine(withdrawal, path), simulator.Survives(withdrawal, path));
        }

        // --- End to end ---

        [Fact]
        public void Optimize_RecommendedSpend_SurvivesBetween80And85Percent_AndIsRepeatable()
        {
            var inputs = new OptimizationInputs { Template = Template(), SocialSecurity = Defaults, Scenarios = new[] { Volatile }, Paths = 200 };

            var first = SpendingOptimizer.Optimize(inputs);
            var second = SpendingOptimizer.Optimize(inputs);

            var optimum = Assert.Single(first.Scenarios);
            Assert.InRange(optimum.VerifiedSurvivalRate, 0.80, 0.85);
            Assert.True(optimum.Recommended.SpendAt85 <= optimum.Recommended.SpendAtMidpoint);
            Assert.True(optimum.Recommended.SpendAtMidpoint <= optimum.Recommended.SpendAt80);
            Assert.Equal(9, optimum.ClaimingAges.Count);
            Assert.Equal(optimum.Recommended, second.Scenarios[0].Recommended);
            Assert.Equal(optimum.ClaimingAges, second.Scenarios[0].ClaimingAges);
        }

        [Fact]
        public void Optimize_WithNoSocialSecurity_AllAgesTie_AndTheEarliestIsRecommended()
        {
            var inputs = new OptimizationInputs { Template = Template(), SocialSecurity = new(0, 0, 0), Scenarios = new[] { Flat }, Paths = 5 };

            var optimum = SpendingOptimizer.Optimize(inputs).Scenarios[0];

            Assert.Single(optimum.ClaimingAges.Select(a => a.SpendAtMidpoint).Distinct());
            Assert.Equal(62, optimum.Recommended.Age);
        }

        [Fact]
        public void Optimize_WhenOnlyDelayingPays_Recommends70()
        {
            // Nothing before 67 and a steep climb to 70: over a 30-year horizon waiting wins, and the savings easily
            // bridge the gap. (With an enormous age-70 benefit the bridge itself would bind, favoring a slightly
            // earlier start - so this uses a realistic size.)
            var inputs = new OptimizationInputs { Template = Template(), SocialSecurity = new(0, 0, 5_000), Scenarios = new[] { Flat }, Paths = 5 };

            var optimum = SpendingOptimizer.Optimize(inputs).Scenarios[0];

            Assert.Equal(70, optimum.Recommended.Age);
            Assert.Equal(new DateOnly(2036, 1, 1), optimum.Recommended.StartDate);
        }

        [Fact]
        public void Optimize_RecommendsTheAgeWithTheHighestMidpointSpend()
        {
            var inputs = new OptimizationInputs { Template = Template(), SocialSecurity = Defaults, Scenarios = new[] { Flat }, Paths = 5 };

            var optimum = SpendingOptimizer.Optimize(inputs).Scenarios[0];

            Assert.Equal(optimum.ClaimingAges.Max(a => a.SpendAtMidpoint), optimum.Recommended.SpendAtMidpoint);
        }

        // --- Isolation guard ---

        [Fact]
        public void ParametersCopy_CopiesEverySettableProperty()
        {
            var original = Template();
            // Give every settable property a non-default value, so a property the copy forgets shows up.
            foreach (var property in typeof(SimulationParameters).GetProperties().Where(p => p.CanWrite))
            {
                object value = property.PropertyType switch
                {
                    var t when t == typeof(int) => 7,
                    var t when t == typeof(double) => 7.5,
                    var t when t == typeof(bool) => true,
                    var t when t == typeof(DateOnly) => new DateOnly(2031, 3, 4),
                    var t when t == typeof(string) => "copied",
                    var t when t == typeof(WithdrawalStrategy) => WithdrawalStrategy.TaxOptimized,
                    var t => throw new InvalidOperationException($"Add a test value for {t.Name} ({property.Name})")
                };
                property.SetValue(original, value);
            }

            var copy = ParametersCopy.Of(original);

            foreach (var property in typeof(SimulationParameters).GetProperties().Where(p => p.CanWrite))
                Assert.True(Equals(property.GetValue(original), property.GetValue(copy)), $"{property.Name} was not copied");
        }
    }
}
