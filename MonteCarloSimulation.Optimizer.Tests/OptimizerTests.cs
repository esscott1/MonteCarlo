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

        [Fact]
        public void FromFullRetirementAmount_FollowsSsaRules_70PercentAt62And124PercentAt70()
        {
            var curve = SocialSecurityCurve.FromFullRetirementAmount(3_900);

            Assert.Equal(2_730, curve.MonthlyBenefitAtAge(62), 9);
            Assert.Equal(3_900, curve.MonthlyBenefitAtAge(67), 9);
            Assert.Equal(4_836, curve.MonthlyBenefitAtAge(70), 9);
            // 64 is 36 months early: 20% less
            Assert.Equal(3_900 * 0.8, curve.MonthlyBenefitAtAge(64), 9);
        }

        [Theory]
        [InlineData(62, 0)]
        [InlineData(64, 3)]
        [InlineData(67, 0)]
        [InlineData(69, 6)]
        [InlineData(70, 0)]
        public void FromClaim_PaysTheClaimsOwnAmountAtItsOwnAge_AndFollowsSsaRulesElsewhere(int years, int months)
        {
            var birthdate = new DateOnly(1969, 7, 7);
            var start = birthdate.AddYears(years).AddMonths(months);

            var curve = SocialSecurityCurve.FromClaim(birthdate, start, 2_750);

            Assert.Equal(2_750, curve.MonthlyBenefitAtMonths(years * 12 + months), 9);
            Assert.Equal(curve.At67 * SocialSecurityCurve.SsaShareAt62, curve.At62, 9);
            Assert.Equal(curve.At67 * SocialSecurityCurve.SsaShareAt70, curve.At70, 9);
            // A day short of the month counts the month before (and never earlier than 62)
            var dayShort = SocialSecurityCurve.FromClaim(birthdate, start.AddDays(-1), 2_750);
            Assert.Equal(2_750, dayShort.MonthlyBenefitAtMonths(Math.Max(62 * 12, years * 12 + months - 1)), 9);
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

        // --- Annuity purchases ---

        // A household the purchase search has room to work with: $900,000 in Tax Deferred, retiring at 60
        private static OptimizationInputs WithPurchase(double monthlyPer100k, int paths = 60) => new()
        {
            Template = Mixed(),
            SocialSecurity = Defaults,
            Paths = paths,
            Purchase = new AnnuityPurchaseOptions(monthlyPer100k, "2026-10-01")
        };

        [Fact]
        public void APurchase_ComesOutOfTaxDeferred_AndPaysTheRateForItsSize()
        {
            var options = new AnnuityPurchaseOptions(612, "2026-10-01");
            var template = Mixed();

            var bought = AnnuityPurchase.Apply(template, options, 400_000);

            Assert.Equal(500_000, bought.InitialTaxableBalance);
            Assert.Equal(900_000, template.InitialTaxableBalance); // the template itself is untouched
            Assert.Equal(2_448, bought.Annuity!.MonthlyPayment);
            Assert.Equal(template.RetirementDate, bought.Annuity.FirstPayment); // from the Retire On date, like the runner's SPIA
            Assert.Equal(0, bought.Annuity.Basis); // qualified: every payment is ordinary income
            Assert.Equal(0, bought.Annuity.AnnualIncrease);
            Assert.Equal(template.InitialBrokerageBasis, bought.InitialBrokerageBasis);
            Assert.Equal(Enumerable.Range(1, 9).Select(k => k * 100_000.0), AnnuityPurchase.Amounts(template, options));
        }

        [Fact]
        public void NoSizesToTry_WithAnOwnedAnnuity_ARateOfZero_OrNoTaxDeferred()
        {
            var options = new AnnuityPurchaseOptions(612, "2026-10-01");
            var owned = Mixed();
            owned.Annuity = new Annuity { MonthlyPayment = 1_000, FirstPayment = owned.RetirementDate };
            var empty = Mixed();
            empty.InitialTaxableBalance = 99_000;

            Assert.Empty(AnnuityPurchase.Amounts(owned, options));
            Assert.Empty(AnnuityPurchase.Amounts(Mixed(), options with { MonthlyPer100k = 0 }));
            Assert.Empty(AnnuityPurchase.Amounts(empty, options));
        }

        [Fact]
        public void WithoutSizesToTry_TheResultIsExactlyTheSearchWithoutPurchases()
        {
            var plain = SpendingOptimizer.Optimize(new OptimizationInputs { Template = Mixed(), SocialSecurity = Defaults, Paths = 60 }).Optimum;
            var zeroRate = SpendingOptimizer.Optimize(WithPurchase(0)).Optimum;

            Assert.Null(plain.Purchase);
            Assert.Null(zeroRate.Purchase);
            Assert.Equal(plain.Recommended, zeroRate.Recommended);
            Assert.Equal(plain.VerifiedSurvivalRate, zeroRate.VerifiedSurvivalRate);
            Assert.Equal(plain.ClaimingAges, zeroRate.ClaimingAges);
        }

        [Fact]
        public void AGoodRate_SuggestsAPurchase_ThatRaisesTheSpend_AndThePlanIsThatPurchasesOwnSearch()
        {
            var inputs = WithPurchase(700);

            var optimum = SpendingOptimizer.Optimize(inputs).Optimum;
            var decision = optimum.Purchase!;

            Assert.True(decision.Amount > 0);
            Assert.Equal(0, decision.Amount % 100_000);
            Assert.Equal(700 * decision.Amount / 100_000, decision.MonthlyPayment, 6);
            Assert.True(decision.SpendWith > decision.SpendWithout);
            Assert.Equal(optimum.Recommended.SpendAtMidpoint, decision.SpendWith);
            Assert.Equal(9, decision.AmountsConsidered);
            Assert.Equal("2026-10-01", decision.RateAsOf);

            // The recommendation is what searching the purchased plan on its own finds
            var bought = AnnuityPurchase.Apply(inputs.Template, inputs.Purchase!, decision.Amount);
            var alone = SpendingOptimizer.Optimize(new OptimizationInputs { Template = bought, SocialSecurity = Defaults, Paths = 60 }).Optimum;
            Assert.Equal(alone.Recommended, optimum.Recommended);
            Assert.Equal(alone.ClaimingAges, optimum.ClaimingAges);
            Assert.Equal(alone.VerifiedSurvivalRate, optimum.VerifiedSurvivalRate);

            // And the plan without it is the plain search's
            var plain = SpendingOptimizer.Optimize(new OptimizationInputs { Template = Mixed(), SocialSecurity = Defaults, Paths = 60 }).Optimum;
            Assert.Equal(plain.Recommended.SpendAtMidpoint, decision.SpendWithout);
            Assert.True(decision.MoneyLeftWithout > 0 && decision.MoneyLeftWith > 0);
        }

        [Fact]
        public void APoorRate_SuggestsNothing_AndKeepsThePlanWithoutAPurchase()
        {
            var optimum = SpendingOptimizer.Optimize(WithPurchase(1)).Optimum;
            var plain = SpendingOptimizer.Optimize(new OptimizationInputs { Template = Mixed(), SocialSecurity = Defaults, Paths = 60 }).Optimum;

            var decision = optimum.Purchase!;
            Assert.Equal(0, decision.Amount);
            Assert.Equal(0, decision.MonthlyPayment);
            Assert.Equal(decision.SpendWithout, decision.SpendWith);
            Assert.Equal(decision.MoneyLeftWithout, decision.MoneyLeftWith);
            Assert.Equal(plain.Recommended, optimum.Recommended);
            Assert.Equal(plain.ClaimingAges, optimum.ClaimingAges);
        }

        [Fact]
        public void WithPurchases_TheListenerHearsEveryStep_AndTheResultIsRepeatable()
        {
            var inputs = WithPurchase(700, paths: 40);
            var progress = new List<(int Completed, int Total)>();
            var listener = new OptimizationListener { StepDone = (completed, total) => { lock (progress) progress.Add((completed, total)); } };

            var reported = SpendingOptimizer.Optimize(inputs, listener, CancellationToken.None).Optimum;
            var again = SpendingOptimizer.Optimize(inputs).Optimum;

            // 9 claiming ages without a purchase, the screen, then 9 for each of the 2 finalists
            int total = SpendingOptimizer.TotalSteps(inputs);
            Assert.Equal(9 + 1 + 2 * 9, total);
            Assert.Equal(Enumerable.Range(1, total), progress.Select(p => p.Completed).Order());
            Assert.All(progress, p => Assert.Equal(total, p.Total));
            Assert.Equal(again, reported with { ClaimingAges = again.ClaimingAges });
            Assert.Equal(again.ClaimingAges, reported.ClaimingAges);
        }

        [Fact]
        public void AColaPurchase_StartsTenPercentLower_AndRisesTwoPercentEachJanuary()
        {
            var options = new AnnuityPurchaseOptions(612, "2026-10-01", ConsiderCola: true);
            var template = Mixed();

            var bought = AnnuityPurchase.Apply(template, options, 400_000, cola: true);

            Assert.Equal(500_000, bought.InitialTaxableBalance);
            Assert.Equal(2_203.2, bought.Annuity!.MonthlyPayment, 6); // 90% of the level 2,448
            Assert.Equal(Annuity.ColaIncrease, bought.Annuity.AnnualIncrease);
            Assert.Equal(0, bought.Annuity.Basis);
            // Every size, level and then with the COLA; level only without ConsiderCola
            var candidates = AnnuityPurchase.Candidates(template, options);
            Assert.Equal(18, candidates.Count);
            Assert.Equal(9, candidates.Count(c => c.Cola));
            Assert.All(AnnuityPurchase.Candidates(template, options with { ConsiderCola = false }), c => Assert.False(c.Cola));
        }

        [Fact]
        public void WithColasWeighed_AColaCanWin_AndThePlanIsThatPurchasesOwnSearch()
        {
            var level = WithPurchase(700);
            var inputs = new OptimizationInputs { Template = level.Template, SocialSecurity = level.SocialSecurity, Paths = level.Paths, Purchase = level.Purchase! with { ConsiderCola = true } };

            var optimum = SpendingOptimizer.Optimize(inputs).Optimum;
            var decision = optimum.Purchase!;

            // Still the screen plus two finalists: the best level SPIA and the best COLA one
            Assert.Equal(9 + 1 + 2 * 9, SpendingOptimizer.TotalSteps(inputs));
            Assert.Equal(18, decision.AmountsConsidered);
            // Over this household's 30 years of inflation the COLA's later payments win, and beat the best level purchase
            Assert.Equal(Annuity.ColaIncrease, decision.AnnualIncrease);
            Assert.True(decision.Amount > 0);
            Assert.Equal(630 * decision.Amount / 100_000, decision.MonthlyPayment, 6);
            Assert.True(decision.SpendWith > SpendingOptimizer.Optimize(level).Optimum.Purchase!.SpendWith);

            var bought = AnnuityPurchase.Apply(inputs.Template, inputs.Purchase!, decision.Amount, cola: true);
            var alone = SpendingOptimizer.Optimize(new OptimizationInputs { Template = bought, SocialSecurity = Defaults, Paths = 60 }).Optimum;
            Assert.Equal(alone.Recommended, optimum.Recommended);
            Assert.Equal(alone.VerifiedSurvivalRate, optimum.VerifiedSurvivalRate);
        }

        [Fact]
        public void AnAfterTaxPurchase_SellsBrokerageFirst_AfterItsEstimatedTax_ThenRothBasis_ThenRothGains()
        {
            var options = new AnnuityPurchaseOptions(612, "2026-10-01", ConsiderAfterTax: true);
            var template = Mixed(); // Brokerage $200,000 basis + $200,000 gain, Roth $50,000 + $50,000, 60 on the Retire On date

            // $100,000 from Brokerage: half of the sale is gain, and $50,000 of gain stays inside the 0% band
            var small = AnnuityPurchase.Source(template, 100_000, AnnuityFunding.AfterTax);
            Assert.Equal(AnnuityFunding.AfterTax, small.Funding);
            Assert.Equal(0, small.BrokerageSaleTax);
            Assert.Equal(50_000, small.FromBrokerageBasis, 6);
            Assert.Equal(50_000, small.FromBrokerageGain, 6);
            Assert.Equal(0, small.FromTaxDeferred + small.FromRothBasis + small.FromRothGain);

            var bought = AnnuityPurchase.Apply(template, options, 100_000, funding: AnnuityFunding.AfterTax);
            Assert.Equal(900_000, bought.InitialTaxableBalance); // Tax Deferred untouched
            Assert.Equal(150_000, bought.InitialBrokerageBasis, 6);
            Assert.Equal(150_000, bought.InitialBrokerageUnrealizedGain, 6);
            Assert.Equal(template.InitialRothBasis, bought.InitialRothBasis);
            Assert.Equal(100_000, bought.Annuity!.Basis); // non-qualified: its price comes back tax-free
            Assert.Equal(612, bought.Annuity.MonthlyPayment, 6);

            // More than Brokerage can net: all of it is sold (its gains past the 0% band taxed at 15%), then Roth basis, then
            // Roth gains
            var large = AnnuityPurchase.Source(template, 450_000, AnnuityFunding.AfterTax);
            Assert.Equal(400_000, large.FromBrokerageBasis + large.FromBrokerageGain, 6);
            Assert.True(large.BrokerageSaleTax > 0);
            Assert.Equal(50_000, large.FromRothBasis, 6);
            Assert.Equal(450_000, large.FromBrokerageBasis + large.FromBrokerageGain - large.BrokerageSaleTax + large.FromRothBasis + large.FromRothGain, 6);
            Assert.True(large.FromRothGain > 0 && large.FromRothGain < 50_000);
            var drained = AnnuityPurchase.Apply(template, options, 450_000, funding: AnnuityFunding.AfterTax);
            Assert.Equal(0, drained.InitialBrokerageBasis + drained.InitialBrokerageUnrealizedGain, 6);
            Assert.Equal(0, drained.InitialRothBasis, 6);

            // The sizes reach what Brokerage nets (about $380,000) plus all of Roth: $400,000 in $100,000 steps
            Assert.Equal(new[] { 100_000.0, 200_000, 300_000, 400_000 }, AnnuityPurchase.AfterTaxAmounts(template, options));
            // A Tax Deferred purchase is the one it always was
            Assert.Equal(AnnuityPurchase.Apply(template, options with { ConsiderAfterTax = false }, 400_000).InitialTaxableBalance,
                AnnuityPurchase.Apply(template, options, 400_000).InitialTaxableBalance);
        }

        [Fact]
        public void BeforeFiftyNineAndAHalf_OnlyRothBasisPaysForAnAfterTaxPurchase()
        {
            var options = new AnnuityPurchaseOptions(612, "2026-10-01", ConsiderAfterTax: true);
            var young = Mixed();
            young.Birthdate = new DateOnly(1970, 1, 1); // 56 on the Retire On date

            var source = AnnuityPurchase.Source(young, 450_000, AnnuityFunding.AfterTax);

            Assert.Equal(50_000, source.FromRothBasis, 6);
            Assert.Equal(0, source.FromRothGain);
            // Brokerage nets about $380,000 and Roth adds only its $50,000 basis: four sizes, and $450,000 can't be paid for
            Assert.Equal(4, AnnuityPurchase.AfterTaxAmounts(young, options).Count);
            Assert.True(source.FromBrokerageBasis + source.FromBrokerageGain - source.BrokerageSaleTax + source.FromRothBasis < 450_000);
        }

        [Fact]
        public void WithAfterTaxMoneyWeighed_EachKindIsAFinalist_AndThePlanIsThatPurchasesOwnSearch()
        {
            var level = WithPurchase(700);
            var inputs = new OptimizationInputs
            {
                Template = level.Template, SocialSecurity = level.SocialSecurity, Paths = level.Paths,
                Purchase = level.Purchase! with { ConsiderCola = true, ConsiderAfterTax = true }
            };

            var optimum = SpendingOptimizer.Optimize(inputs).Optimum;
            var decision = optimum.Purchase!;

            // The screen, then the best of each kind: level or COLA, Tax Deferred or after-tax money
            Assert.Equal(9 + 1 + 4 * 9, SpendingOptimizer.TotalSteps(inputs));
            Assert.Equal(9 + 1 + 2 * 9, SpendingOptimizer.TotalSteps(new OptimizationInputs
            {
                Template = level.Template, SocialSecurity = level.SocialSecurity, Paths = level.Paths,
                Purchase = level.Purchase! with { ConsiderAfterTax = true }
            }));
            Assert.Equal(2 * (9 + 4), decision.AmountsConsidered);
            Assert.True(decision.Amount > 0);
            var source = decision.Source!;
            Assert.Equal(AnnuityPurchase.Source(inputs.Template, decision.Amount, source.Funding), source);

            var bought = AnnuityPurchase.Apply(inputs.Template, inputs.Purchase!, decision.Amount, decision.AnnualIncrease > 0, source.Funding);
            var alone = SpendingOptimizer.Optimize(new OptimizationInputs { Template = bought, SocialSecurity = Defaults, Paths = 60 }).Optimum;
            Assert.Equal(alone.Recommended, optimum.Recommended);
            Assert.Equal(alone.VerifiedSurvivalRate, optimum.VerifiedSurvivalRate);
        }

        // --- Optimize Social Security: the best claiming age at a fixed spend ---

        [Fact]
        public void BestClaim_IsTheBirthdayWhereTheMostPathsSurvive_AndCountsTheCurrentClaimOnTheSamePaths()
        {
            var template = Mixed();
            template.SocialSecurityStartDate = template.Birthdate.AddYears(62);
            template.SocialSecurityMonthlyAmount = Defaults.MonthlyBenefitAtAge(62);
            // A spend some markets survive and some don't, so the start date matters
            double spend = SpendingOptimizer.Optimize(new OptimizationInputs { Template = Mixed(), SocialSecurity = Defaults, Paths = 60 }).Optimum.Recommended.SpendAtMidpoint;

            var choice = SocialSecurityOptimizer.BestClaim(template, Defaults, spend, paths: 60);

            Assert.Equal(60, choice.Paths);
            Assert.Equal(SpendingOptimizer.ClaimingAges, choice.ByAge.Select(c => c.Age));
            Assert.All(choice.ByAge, c => Assert.Equal(template.Birthdate.AddYears(c.Age), c.StartDate));
            Assert.All(choice.ByAge, c => Assert.Equal(Defaults.MonthlyBenefitAtAge(c.Age), c.MonthlyBenefit));
            // The best is the most survivors (ties: more money left, then earlier)
            Assert.Equal(choice.ByAge.Max(c => c.Survivors), choice.Best.Survivors);
            Assert.Contains(choice.Best, choice.ByAge);
            // Each age's count is its own paths at that spend, with the order and conversion line it reports
            var market = PathSimulator.SeededPaths(template, 60);
            var simulator = new PathSimulator(template, choice.Best.StartDate, choice.Best.MonthlyBenefit,
                choice.Best.WithdrawalStrategy, choice.Best.RothConversionTarget, market);
            Assert.Equal(choice.Best.Survivors, Enumerable.Range(0, 60).Count(path => simulator.Survives(spend, path)));
            Assert.Equal(choice.Best.Survivors / 60.0, choice.Best.SurvivalRate, 9);
            // The current claim (at 62, the curve's 62 amount) on the same paths is the 62 row
            Assert.Equal(choice.ByAge[0].Survivors, choice.Current.Survivors);
            Assert.Equal(choice.Best.Survivors > choice.Current.Survivors, choice.Improves);
        }

        // --- Reporting progress and cancelling ---

        [Fact]
        public void Listener_HearsEveryClaimingAge_AndTheResultIsUnchanged()
        {
            var inputs = new OptimizationInputs { Template = Mixed(), SocialSecurity = Defaults, Paths = 40 };
            var progress = new List<(int Completed, int Total)>();
            var listener = new OptimizationListener
            {
                StepDone = (completed, total) => { lock (progress) progress.Add((completed, total)); }
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
                StepDone = (done, _) =>
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
                    var t when t == typeof(FilingStatus) => FilingStatus.MarriedJoint,
                    var t when t == typeof(AssetMix) => new AssetMix { StockWeight = 0.7, StockMean = 0.07 },
                    var t when t == typeof(Annuity) => new Annuity { MonthlyPayment = 2_600, Basis = 400_000 },
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
