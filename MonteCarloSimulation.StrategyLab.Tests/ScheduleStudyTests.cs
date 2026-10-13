using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab.Tests
{
    // The schedule study's harness: its candidate set, its schedules run as defined, and its scoring is honest - a
    // schedule that only won on the fit markets doesn't count as a gain out of sample.
    public class ScheduleStudyTests
    {
        [Fact]
        public void TheSchedulesSet_IsTheBaselineTheAppsPairsAndEverySchedule()
        {
            var set = Candidates.Schedules;
            var codes = set.All.Select(c => c.Code).ToList();

            Assert.Equal("W2+C1", set.Baseline.Code);
            Assert.Equal(codes.Count, codes.Distinct().Count());
            Assert.Equal(84, ConversionSchedules.All.Count);                   // 4 x 3 ordered line pairs x 7 conditions
            Assert.Equal(1 + 7 + 84, codes.Count);                             // the baseline is one of the app's 8 pairs
            Assert.All(new[] { "W1", "W2" }.SelectMany(o => new[] { "C0", "C1", "C3", "C4" }.Select(p => $"{o}+{p}")),
                code => Assert.Contains(code, codes));
            Assert.Contains("W2+S24>12@SS", codes);
            Assert.Contains("W2+S0>24@RMD", codes);
        }

        [Fact]
        public void ASchedule_ConvertsOnItsEarlyLineUntilTheConditionEnds()
        {
            // The top of 24% before 63, then nothing
            var combination = Candidates.Schedules.All.Single(c => c.Code == "W2+S24>0@63");
            var scenario = ScenarioGenerator.Generate(40, seed: 11).First(s => s.RetirementAge < 60 && s.TaxDeferredShare > 0.5);
            var runner = new PathRunner(scenario.Parameters, combination);

            var run = runner.Run(scenario.TotalAssets * 0.03, path: 0, out _);

            Assert.Contains(run.Years, y => y.AgeInYear < 63 && y.RothConversionAmount > 0);
            Assert.All(run.Years.Where(y => y.AgeInYear >= 63), y => Assert.Equal(0, y.RothConversionAmount));
        }

        [Fact]
        public void AScheduleThatOnlyWinsOnTheFitMarkets_IsNoGainOutOfSample()
        {
            // Every household: the app's pairs spend 100,000; one schedule spends 110,000 on the fit markets but 95,000
            // on the validation markets; the other schedules 90,000 on both
            var set = Candidates.Schedules;
            var scenarios = ScenarioGenerator.Generate(80, seed: 3);
            const string lucky = "W2+S24>12@SS";
            var appCodes = new[] { "W1", "W2" }.SelectMany(o => new[] { "C0", "C1", "C3", "C4" }.Select(p => $"{o}+{p}")).ToHashSet();
            List<ResultRow> Rows(double luckySpend) => scenarios.SelectMany(s => set.All.Select(c =>
            {
                double spend = appCodes.Contains(c.Code) ? 100_000 : c.Code == lucky ? luckySpend : 90_000;
                return new ResultRow(s.Id, c.Code, spend, spend, 100_000, 0.825, 0, 0);
            })).ToList();

            var study = ScheduleStudy.Run(set, scenarios, Rows(110_000), Rows(95_000), seed: 1);

            var plusOne = study.Rules.Single(r => r.Name == "Today's pick plus 1 schedule");
            Assert.Equal([lucky], plusOne.Schedules);
            Assert.Equal(0.10, plusOne.InSampleMeanGain, 9);      // chosen because it won on the fit markets...
            Assert.Equal(-0.05, plusOne.MeanGain, 9);             // ...and measured on the markets it didn't see
            Assert.False(plusOne.Adopted);
            Assert.DoesNotContain(study.Rules, r => r.Adopted);
            Assert.Equal(40, study.FitHouseholds);
            Assert.Equal(40, study.HoldoutHouseholds);
        }
    }
}
