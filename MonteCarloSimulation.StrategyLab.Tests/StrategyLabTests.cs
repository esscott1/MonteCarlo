using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab.Tests
{
    // Self-checks for the lab harness: it measures what the app does, its candidate orders obey the same rules as
    // the app's, and a seed reproduces its results.
    public class StrategyLabTests
    {
        [Theory]
        [InlineData(WithdrawalStrategy.TaxOptimized, "W2+C1", true)]
        [InlineData(WithdrawalStrategy.TaxOptimized, "W2+C0", false)]
        [InlineData(WithdrawalStrategy.ProRata, "W1+C1", true)]
        [InlineData(WithdrawalStrategy.ProRata, "W1+C0", false)]
        public void AppCombinations_SurviveExactlyWhenTheEngineDoes(WithdrawalStrategy strategy, string code, bool conversions)
        {
            var combination = Candidates.All.Single(c => c.Code == code);
            foreach (var scenario in ScenarioGenerator.Generate(12, seed: 7))
            {
                var runner = new PathRunner(scenario.Parameters, combination);
                double spend = runner.BreakEven(0);
                foreach (double withdrawal in new[] { spend * 0.8, spend, spend + 2 * PathRunner.Precision, spend * 1.2 })
                {
                    var p = Scenario.Copy(scenario.Parameters);
                    p.Withdrawal = withdrawal;
                    p.Iterations = 1;
                    p.WithdrawalStrategy = strategy;
                    p.EnableRothConversions = conversions;
                    p.RothConversionTarget = RothConversionTarget.Bracket12;
                    for (int path = 0; path < 5; path++)
                    {
                        bool engineSurvives = MonteCarloEngine.Run(p, new Random(path)).Result.OutOfMoneyCount == 0;
                        Assert.Equal(engineSurvives, runner.Survives(withdrawal, path));
                    }
                }
            }
        }

        [Fact]
        public void CandidateOrders_RespectBalancesAndAgeGate_AndNetTheNeed()
        {
            var rng = new Random(11);
            // Not W1: the app's Pro-rata grosses up Tax Deferred's proportional share without capping it at the
            // balance, and the engine then fails that run on the negative balance - app behavior, not the lab's.
            var orders = Candidates.Orders.Where(o => o != Candidates.ProRata).Append(Candidates.TaxOptimizedNoHarvest).ToList();
            for (int i = 0; i < 3_000; i++)
            {
                double inflation = 1 + rng.NextDouble();
                var taxYear = new TaxYear(16_000 * inflation, inflation, FederalTaxBrackets.Single2026, FederalTaxBrackets.CapitalGainsSingle2026);
                bool ageEligible = rng.NextDouble() < 0.7;
                double taxDeferred = rng.NextDouble() < 0.15 ? 0 : rng.NextDouble() * 2_000_000;
                double roth = rng.NextDouble() < 0.3 ? 0 : rng.NextDouble() * 400_000;
                var context = new WithdrawalContext(
                    Need: rng.NextDouble() * 300_000,
                    SsTaxable: rng.NextDouble() < 0.4 ? 0 : rng.NextDouble() * 60_000,
                    TaxYear: taxYear,
                    EligibleTaxable: ageEligible ? taxDeferred : 0,
                    Brokerage: rng.NextDouble() < 0.15 ? 0 : rng.NextDouble() * 1_500_000,
                    GainFraction: rng.NextDouble(),
                    EligibleRoth: ageEligible ? roth : roth * 0.5);

                foreach (var order in orders)
                {
                    var plan = order.Strategy.Plan(context);
                    Assert.True(plan.GrossTaxable <= context.EligibleTaxable + 1e-6, $"{order.Code} overdrew Tax Deferred");
                    Assert.True(plan.GrossBrokerage <= context.Brokerage + 1e-6, $"{order.Code} overdrew Brokerage");
                    Assert.True(plan.Roth <= context.EligibleRoth + 1e-6, $"{order.Code} overdrew Roth");
                    Assert.True(plan.GrossTaxable >= 0 && plan.GrossBrokerage >= 0 && plan.Roth >= 0);
                    if (!ageEligible) Assert.Equal(0, plan.GrossTaxable);

                    // Cash after the year's exact taxes covers the need, unless the plan reports a shortfall
                    double ssTax = taxYear.OrdinaryTax(context.SsTaxable);
                    double cash = plan.GrossTaxable + plan.GrossBrokerage + plan.Roth
                        - (taxYear.TotalTax(context.SsTaxable + plan.GrossTaxable, plan.RealizedGains) - ssTax);
                    if (!plan.IsShortfall)
                        Assert.True(Math.Abs(cash - context.Need) < 0.02, $"{order.Code} netted {cash:F4} for a need of {context.Need:F4}");
                    else
                        Assert.True(cash < context.Need, $"{order.Code} reported a shortfall it didn't have");
                }
            }
        }

        [Fact]
        public void SameSeed_ReproducesScenariosAndResults()
        {
            var a = ScenarioGenerator.Generate(20, seed: 99);
            var b = ScenarioGenerator.Generate(20, seed: 99);
            Assert.Equal(a.Select(s => (s.TotalAssets, s.RetirementAge, s.Parameters.RetirementDate)),
                         b.Select(s => (s.TotalAssets, s.RetirementAge, s.Parameters.RetirementDate)));
            Assert.NotEqual(a[0].TotalAssets, ScenarioGenerator.Generate(1, seed: 100)[0].TotalAssets);

            var combos = Candidates.All.Take(3).ToList();
            var first = ScenarioEvaluator.Evaluate(a[0], combos, paths: 20);
            var second = ScenarioEvaluator.Evaluate(b[0], combos, paths: 20);
            Assert.Equal(first, second);
        }

        [Fact]
        public void BreakEven_IsTheLastSurvivingSpend_WithOrWithoutAHint()
        {
            var scenario = ScenarioGenerator.Generate(3, seed: 5)[1];
            var runner = new PathRunner(scenario.Parameters, Candidates.All.Single(c => c.Code == "W4+C3"));
            for (int path = 0; path < 10; path++)
            {
                double cold = runner.BreakEven(path);
                double hinted = runner.BreakEven(path, cold * 1.4);
                Assert.True(runner.Survives(cold, path));
                Assert.False(runner.Survives(cold + PathRunner.Precision, path));
                Assert.True(Math.Abs(cold - hinted) <= PathRunner.Precision);
            }
        }

        [Fact]
        public void ScenarioCopy_CopiesEverySettableProperty()
        {
            var original = ScenarioGenerator.Generate(1, seed: 1)[0].Parameters;
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
                    var t when t == typeof(WithdrawalStrategy) => WithdrawalStrategy.ProRata,
                    var t when t == typeof(ConversionTaxFunding) => ConversionTaxFunding.BridgeAware,
                    var t when t == typeof(RothConversionTarget) => RothConversionTarget.Bracket24,
                    var t when t == typeof(AssetMix) => new AssetMix { StockWeight = 0.7, StockMean = 0.07 },
                    var t => throw new InvalidOperationException($"Add a test value for {t.Name} ({property.Name})")
                };
                property.SetValue(original, value);
            }

            var copy = Scenario.Copy(original);

            foreach (var property in typeof(SimulationParameters).GetProperties().Where(p => p.CanWrite))
                Assert.True(Equals(property.GetValue(original), property.GetValue(copy)), $"{property.Name} was not copied");
        }

        [Fact]
        public void FundingSet_ComparesEveryRule_AgainstTodaysBrokerageOnlyBaseline()
        {
            var set = Candidates.Funding;
            Assert.Equal("W2+C1+F0", set.Baseline.Code);
            Assert.Equal(set.All.Count, set.All.Select(c => c.Code).Distinct().Count());
            // Pro-rata and Tax-optimized x 4 converting policies x 4 rules, plus both orders without conversions
            Assert.Equal(2 * 4 * 4 + 2, set.All.Count);
            Assert.Contains(set.All, c => c.Code == set.ProRataCode);
        }

        [Theory]
        [InlineData(ConversionTaxFunding.Brokerage)]
        [InlineData(ConversionTaxFunding.FromConversion)]
        [InlineData(ConversionTaxFunding.BridgeAware)]
        public void FundingCombination_RunsExactlyLikeTheEngineWithThatRule(ConversionTaxFunding rule)
        {
            var combination = Candidates.Funding.All.Single(c => c.Order == Candidates.TaxOptimized && c.Policy == Candidates.AppDefault && c.Funding!.Value == rule);
            foreach (var scenario in ScenarioGenerator.Generate(8, seed: 3))
            {
                var runner = new PathRunner(scenario.Parameters, combination);
                var p = Scenario.Copy(scenario.Parameters);
                p.Withdrawal = scenario.TotalAssets * 0.045;
                p.Iterations = 1;
                p.EnableRothConversions = true;
                p.ConversionTaxFunding = rule;
                p.RothConversionTarget = RothConversionTarget.Bracket12;
                p.WithdrawalStrategy = WithdrawalStrategy.TaxOptimized;
                for (int path = 0; path < 5; path++)
                    Assert.Equal(MonteCarloEngine.Run(p, new Random(path)).Result.Runs[0].Years, runner.Run(p.Withdrawal, path, out _).Years);
            }
        }

        [Fact]
        public void EveryCombinationCode_HasADefinition()
        {
            var defined = Candidates.Definitions().Select(d => d.Code).ToHashSet();
            foreach (var combination in Candidates.All)
            {
                Assert.Contains(combination.Order.Code, defined);
                Assert.Contains(combination.Policy.Code, defined);
            }

            // The conversion lines are real 2026 dollar amounts: deduction plus the bracket threshold
            var lines = Candidates.Definitions().Where(d => d.Kind == "conversion").ToDictionary(d => d.Code, d => d.Line2026);
            Assert.Null(lines["C0"]);
            Assert.Equal(66_400, lines["C1"]);
            Assert.Equal(66_400, lines["C2"]);
            Assert.Equal(121_700, lines["C3"]);
            Assert.Equal(217_775, lines["C4"]);
        }

        [Fact]
        public void PublishedModelInfoData_IsACurrentSummary()
        {
            // The Model Info page's data, committed in the web app: it must deserialize to the lab's current Summary
            // shape and cover every combination, so a stale or hand-edited file fails here.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MonteCarlo.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var path = Path.Combine(dir!.FullName, SummaryJson.PublishedPath);

            var summary = System.Text.Json.JsonSerializer.Deserialize<Summary>(File.ReadAllText(path), SummaryJson.Options);

            Assert.NotNull(summary);
            Assert.Equal(Candidates.All.Select(c => c.Code).OrderBy(c => c), summary!.VsBaseline.Select(c => c.Code).OrderBy(c => c));
            Assert.Equal(Candidates.Definitions().Count, summary.Definitions.Count);
            Assert.Equal(Candidates.Baseline.Code, summary.Baseline);
            Assert.Equal(4, summary.Subsets.Count);
            Assert.Equal(4, summary.PageDefault.Count);
            Assert.NotNull(summary.CaseStudy);
            Assert.True(summary.ScenariosCompared > 0);
        }

        [Fact]
        public void SpendAtSurvival_ReadsThePercentileOffSortedBreakEvens()
        {
            var sorted = Enumerable.Range(1, 200).Select(i => i * 100.0).ToArray();
            // 82.5% of 200 = 165 paths must survive: the spend is the 36th smallest break-even
            Assert.Equal(3_600, ScenarioEvaluator.SpendAtSurvival(sorted, 0.825));
            Assert.Equal(10_100, ScenarioEvaluator.SpendAtSurvival(sorted, 0.5));
        }
    }
}
