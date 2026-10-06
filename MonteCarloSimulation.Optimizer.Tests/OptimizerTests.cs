using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer.Tests
{
    public class OptimizerTests
    {
        // The markets these tests optimize for: the template's own. Volatile is a single-return market like 95 years
        // of the S&P; Flat is 4% a year with no volatility; DefaultMix is the Optimal page's default asset mix.
        private const double VolatileMean = 0.0807, VolatileStdDev = 0.1915;

        private static readonly AssetMix DefaultMix = new()
        {
            StockWeight = 0.6, BondWeight = 0.3, CashWeight = 0.1,
            StockMean = 0.08, StockStdDev = 0.19,
            BondMean = 0.045, BondStdDev = 0.04,
            CashMean = 0.035, CashStdDev = 0.01,
            StockBondCorrelation = 0.1
        };

        private static SimulationParameters Template(double mean, double stdDev, AssetMix? mix = null, int years = 30) => new()
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
            Mean = mean,
            StdDev = stdDev,
            AssetMix = mix,
            ScenarioDescription = "optimizer test"
        };

        private static SimulationParameters Volatile() => Template(VolatileMean, VolatileStdDev);
        private static SimulationParameters Flat() => Template(0.04, 0);
        private static SimulationParameters Mixed() => Template(DefaultMix.ExpectedReturn, DefaultMix.StdDev, DefaultMix);

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
            var simulator = new PathSimulator(Flat(), new DateOnly(2033, 1, 1), 3_900, WithdrawalStrategy.TaxOptimized, RothConversionTarget.Bracket12);

            double breakEven = simulator.BreakEven(0);

            Assert.True(breakEven > 0);
            Assert.True(simulator.Survives(breakEven, 0));
            Assert.False(simulator.Survives(breakEven + PathSimulator.Precision, 0));
        }

        // The simulator draws its own path when none were precomputed: the same draw Core's engine makes for that seed,
        // in a single-return market and in an asset mix.
        [Theory]
        [InlineData(40_000, 0, false)]
        [InlineData(80_000, 1, false)]
        [InlineData(120_000, 2, false)]
        [InlineData(95_000, 7, false)]
        [InlineData(60_000, 0, true)]
        [InlineData(85_000, 3, true)]
        [InlineData(110_000, 5, true)]
        public void Survives_MatchesCoresEngineForTheSameSeededPath(double withdrawal, int path, bool mix)
        {
            var simulator = new PathSimulator(mix ? Mixed() : Volatile(), new DateOnly(2033, 1, 1), 3_900, WithdrawalStrategy.TaxOptimized, RothConversionTarget.Bracket12);

            Assert.Equal(simulator.SurvivesViaEngine(withdrawal, path), simulator.Survives(withdrawal, path));
        }

        [Fact]
        public void SeededPaths_AreThePathsTheSimulatorWouldDrawItself()
        {
            var template = Mixed();
            var paths = PathSimulator.SeededPaths(template, 12);
            var shared = new PathSimulator(template, new DateOnly(2033, 1, 1), 3_900, WithdrawalStrategy.TaxOptimized, RothConversionTarget.Bracket12, paths);
            var own = new PathSimulator(template, new DateOnly(2033, 1, 1), 3_900, WithdrawalStrategy.TaxOptimized, RothConversionTarget.Bracket12);

            for (int path = 0; path < 12; path++)
                Assert.Equal(own.BreakEven(path), shared.BreakEven(path));
        }

        // --- End to end ---

        [Fact]
        public void Optimize_RecommendedSpend_SurvivesBetween80And85Percent_AndIsRepeatable()
        {
            var inputs = new OptimizationInputs { Template = Mixed(), SocialSecurity = Defaults, Paths = 200 };

            var first = SpendingOptimizer.Optimize(inputs).Optimum;
            var second = SpendingOptimizer.Optimize(inputs).Optimum;

            Assert.InRange(first.VerifiedSurvivalRate, 0.80, 0.85);
            Assert.True(first.Recommended.SpendAt85 <= first.Recommended.SpendAtMidpoint);
            Assert.True(first.Recommended.SpendAtMidpoint <= first.Recommended.SpendAt80);
            Assert.Equal(9, first.ClaimingAges.Count);
            Assert.Equal(first.Recommended, second.Recommended);
            Assert.Equal(first.ClaimingAges, second.ClaimingAges);
        }

        [Fact]
        public void Optimize_RunsTheTemplatesAssetMix()
        {
            // An all-stock mix at 4% with no volatility is the Flat market, return for return: same answer
            var flatMix = new AssetMix { StockWeight = 1, StockMean = 0.04 };
            var viaMix = SpendingOptimizer.Optimize(new OptimizationInputs { Template = Template(0, 0, flatMix), SocialSecurity = Defaults, Paths = 5 }).Optimum;
            var viaFlat = SpendingOptimizer.Optimize(new OptimizationInputs { Template = Flat(), SocialSecurity = Defaults, Paths = 5 }).Optimum;
            Assert.Equal(viaFlat.ClaimingAges, viaMix.ClaimingAges);

            // ...and a riskier mix changes it: the mix, not Mean/StdDev, decides the market
            var stocks = new AssetMix { StockWeight = 1, StockMean = 0.04, StockStdDev = 0.25 };
            var viaStocks = SpendingOptimizer.Optimize(new OptimizationInputs { Template = Template(0.04, 0, stocks), SocialSecurity = Defaults, Paths = 40 }).Optimum;
            Assert.NotEqual(viaFlat.Recommended.SpendAtMidpoint, viaStocks.Recommended.SpendAtMidpoint);
        }

        [Fact]
        public void Optimize_WithNoSocialSecurity_AllAgesTie_AndTheEarliestIsRecommended()
        {
            var inputs = new OptimizationInputs { Template = Flat(), SocialSecurity = new(0, 0, 0), Paths = 5 };

            var optimum = SpendingOptimizer.Optimize(inputs).Optimum;

            Assert.Single(optimum.ClaimingAges.Select(a => a.SpendAtMidpoint).Distinct());
            Assert.Equal(62, optimum.Recommended.Age);
            Assert.All(optimum.ClaimingAges, a => Assert.Equal(0, a.TotalSocialSecurity));
        }

        // --- Total Social Security collected ---

        [Fact]
        public void TotalSocialSecurity_SumsEveryInflatedPayment_FromTheStartDateToTheEndOfTheWindow()
        {
            // Retire 2026-01-01 for 30 years (window ends 2056-01-01); born 1966, so claiming at 70 starts
            // 2036-01-01: 20 full years of 12 payments. Inflation from 2026 is 2.5% a year through the first 20
            // retirement years (to 2046), then 1%.
            var simulator = new PathSimulator(Flat(), new DateOnly(2036, 1, 1), 4_800, WithdrawalStrategy.TaxOptimized, RothConversionTarget.Bracket12);

            double expected = Enumerable.Range(2036, 20)
                .Sum(year => 4_800 * 12 * Math.Pow(1.025, Math.Min(year - 2026, 20)) * Math.Pow(1.01, Math.Max(0, year - 2046)));

            Assert.Equal(expected, simulator.TotalSocialSecurity, 6);
        }

        [Fact]
        public void TotalSocialSecurity_MatchesTheSocialSecurityCoresRunPaysEachYear()
        {
            // Social Security doesn't depend on the market path, so Core's public (unseeded) Run pays the same
            // amounts; with no spending the run survives all 30 years.
            var parameters = Volatile();
            parameters.Iterations = 1;
            parameters.Withdrawal = 0;
            parameters.SocialSecurityStartDate = new DateOnly(2031, 7, 15); // mid-year start: a partial first year
            parameters.SocialSecurityMonthlyAmount = 3_133;

            var coreYears = MonteCarloEngine.Run(parameters).Result.Runs[0].Years;
            var simulator = new PathSimulator(Volatile(), new DateOnly(2031, 7, 15), 3_133, WithdrawalStrategy.TaxOptimized, RothConversionTarget.Bracket12);

            Assert.Equal(30, coreYears.Count);
            Assert.Equal(coreYears.Sum(y => y.SocialSecurityIncome), simulator.TotalSocialSecurity, 6);
        }

        [Fact]
        public void Optimize_WhenOnlyDelayingPays_Recommends70()
        {
            // Nothing before 67 and a steep climb to 70: over a 30-year horizon waiting wins, and the savings easily
            // bridge the gap. (With an enormous age-70 benefit the bridge itself would bind, favoring a slightly
            // earlier start - so this uses a realistic size.)
            var inputs = new OptimizationInputs { Template = Flat(), SocialSecurity = new(0, 0, 5_000), Paths = 5 };

            var optimum = SpendingOptimizer.Optimize(inputs).Optimum;

            Assert.Equal(70, optimum.Recommended.Age);
            Assert.Equal(new DateOnly(2036, 1, 1), optimum.Recommended.StartDate);
        }

        [Fact]
        public void Optimize_RecommendsTheAgeWithTheHighestMidpointSpend()
        {
            var inputs = new OptimizationInputs { Template = Flat(), SocialSecurity = Defaults, Paths = 5 };

            var optimum = SpendingOptimizer.Optimize(inputs).Optimum;

            Assert.Equal(optimum.ClaimingAges.Max(a => a.SpendAtMidpoint), optimum.Recommended.SpendAtMidpoint);
        }

        // --- Isolation guard ---

        private static SpendOptimum RunOptimal(WithdrawalStrategy order, RothConversionTarget target)
        {
            var template = Volatile();
            template.WithdrawalStrategy = order;
            template.RothConversionTarget = target;
            return SpendingOptimizer.Optimize(new OptimizationInputs
            {
                Template = template,
                SocialSecurity = new SocialSecurityCurve(2_750, 3_900, 4_800),
                Paths = 60
            }).Optimum;
        }

        [Fact]
        public void AutomaticOrder_KeepsTheBetterOrderForEachClaimingAge()
        {
            // With two candidates both get a full search, so every claiming age reports the order with the higher
            // 82.5% spend (the second is warm-started, so it's exact to within the search precision).
            var automatic = RunOptimal(WithdrawalStrategy.Automatic, RothConversionTarget.Bracket12);
            var taxOptimized = RunOptimal(WithdrawalStrategy.TaxOptimized, RothConversionTarget.Bracket12);
            var proRata = RunOptimal(WithdrawalStrategy.ProRata, RothConversionTarget.Bracket12);

            for (int i = 0; i < automatic.ClaimingAges.Count; i++)
            {
                var a = automatic.ClaimingAges[i];
                double best = Math.Max(taxOptimized.ClaimingAges[i].SpendAtMidpoint, proRata.ClaimingAges[i].SpendAtMidpoint);
                Assert.InRange(a.SpendAtMidpoint, best - PathSimulator.Precision, best + PathSimulator.Precision);
                Assert.Contains(a.WithdrawalStrategy, new[] { WithdrawalStrategy.TaxOptimized, WithdrawalStrategy.ProRata });
            }
        }

        [Fact]
        public void TwoStagePick_NeverDoesWorseThanTheDefaultCandidate_AndRecordsWhatItChose()
        {
            // With all 8 (order, target) pairs, the first candidate (Tax-optimized, 12% line) is searched in full and
            // only beaten by a finalist that sustains more.
            var automatic = RunOptimal(WithdrawalStrategy.Automatic, RothConversionTarget.Automatic);
            var baseline = RunOptimal(WithdrawalStrategy.TaxOptimized, RothConversionTarget.Bracket12);

            for (int i = 0; i < automatic.ClaimingAges.Count; i++)
            {
                var a = automatic.ClaimingAges[i];
                Assert.True(a.SpendAtMidpoint >= baseline.ClaimingAges[i].SpendAtMidpoint);
                Assert.NotEqual(WithdrawalStrategy.Automatic, a.WithdrawalStrategy);
                Assert.NotEqual(RothConversionTarget.Automatic, a.RothConversionTarget);
            }
        }

        // --- Reporting progress and cancelling ---

        [Fact]
        public void Listener_HearsEveryClaimingAge_AndTheResultIsUnchanged()
        {
            var inputs = new OptimizationInputs { Template = Mixed(), SocialSecurity = Defaults, Paths = 40 };
            var progress = new List<(int Completed, int Total)>();
            var listener = new OptimizationListener
            {
                ClaimingAgeDone = (completed, total) => { lock (progress) progress.Add((completed, total)); }
            };

            var reported = SpendingOptimizer.Optimize(inputs, listener, CancellationToken.None);
            var plain = SpendingOptimizer.Optimize(inputs);

            // Every claiming age, each count exactly once, against the right total
            int total = SpendingOptimizer.ClaimingAges.Count;
            Assert.Equal(Enumerable.Range(1, total), progress.Select(p => p.Completed).Order());
            Assert.All(progress, p => Assert.Equal(total, p.Total));

            // Reporting doesn't change the answer
            Assert.Equal(plain.Optimum.Recommended, reported.Optimum.Recommended);
            Assert.Equal(plain.Optimum.VerifiedSurvivalRate, reported.Optimum.VerifiedSurvivalRate);
            Assert.Equal(plain.Optimum.ClaimingAges, reported.Optimum.ClaimingAges);
            Assert.Equal(plain.BenefitByAge, reported.BenefitByAge);
        }

        [Fact]
        public void Optimize_WithACancelledToken_Throws()
        {
            var inputs = new OptimizationInputs { Template = Volatile(), SocialSecurity = Defaults, Paths = 40 };

            Assert.ThrowsAny<OperationCanceledException>(() => SpendingOptimizer.Optimize(inputs, null, new CancellationToken(canceled: true)));
        }

        [Fact]
        public void Optimize_CancelledMidRun_StopsTheClaimingAgesStillRunning()
        {
            // Every claiming age at full size: far more work than finishes between the cancel and the next path check
            var inputs = new OptimizationInputs { Template = Mixed(), SocialSecurity = Defaults };
            using var cancel = new CancellationTokenSource();
            int completed = 0;
            var listener = new OptimizationListener
            {
                ClaimingAgeDone = (done, _) =>
                {
                    Interlocked.Increment(ref completed);
                    cancel.Cancel();
                }
            };

            Assert.ThrowsAny<OperationCanceledException>(() => SpendingOptimizer.Optimize(inputs, listener, cancel.Token));
            // Only claiming ages that were already finishing when the first one did can still report
            Assert.InRange(completed, 1, Environment.ProcessorCount);
        }

        [Fact]
        public void ParametersCopy_CopiesEverySettableProperty()
        {
            var original = Flat();
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
                    var t when t == typeof(ConversionTaxFunding) => ConversionTaxFunding.BridgeAware,
                    var t when t == typeof(RothConversionTarget) => RothConversionTarget.Bracket24,
                    var t when t == typeof(AssetMix) => new AssetMix { StockWeight = 0.7, StockMean = 0.07 },
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
