using System.Collections;

namespace MonteCarloSimulation.Core.Tests.Legacy
{
    // Refactor safety net: the same seeded random sequence through the frozen pre-refactor engine and the
    // current engine must produce identical output. Temporary - deleted once the refactor is complete.
    public class EngineEquivalenceTests
    {
        private static SimulationParameters Scenario(
            int age, double withdrawal, WithdrawalStrategy strategy, bool conversions,
            double taxable = 800_000, double rothBasis = 100_000, double rothGain = 100_000,
            double brokerageBasis = 250_000, double brokerageGain = 150_000,
            double mean = 0.07, double stdDev = 0.17, int years = 30, int iterations = 60,
            double newMoney = 0, int yearNewMoney = 0, int ssStart = 0, double ssAmount = 0,
            double standardDeduction = 16_000) => new()
            {
                Years = years,
                Iterations = iterations,
                Withdrawal = withdrawal,
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-age).AddMonths(-3),
                InitialTaxableBalance = taxable,
                InitialRothBasis = rothBasis,
                InitialRothUnrealizedGain = rothGain,
                InitialBrokerageBasis = brokerageBasis,
                InitialBrokerageUnrealizedGain = brokerageGain,
                Mean = mean,
                StdDev = stdDev,
                NewMoney = newMoney,
                YearNewMoney = yearNewMoney,
                SocialSecurityYearsUntilStart = ssStart,
                SocialSecurityAnnualAmount = ssAmount,
                AnnualStandardDeduction = standardDeduction,
                EnableRothConversions = conversions,
                WithdrawalStrategy = strategy,
                ScenarioDescription = "equivalence"
            };

        public static IEnumerable<object[]> Scenarios()
        {
            // Mixed pass/fail, pro-rata, no extras
            yield return new object[] { 1, Outcome.Mixed, Scenario(70, 70_000, WithdrawalStrategy.ProRata, conversions: false) };
            // Tax-optimized with conversions and Social Security starting mid-run
            yield return new object[] { 2, Outcome.Any, Scenario(66, 75_000, WithdrawalStrategy.TaxOptimized, conversions: true, ssStart: 3, ssAmount: 36_000) };
            // Crosses 59.5 mid-run, NewMoney, SS later, conversions on, pro-rata
            yield return new object[] { 3, Outcome.Any, Scenario(52, 60_000, WithdrawalStrategy.ProRata, conversions: true, newMoney: 300_000, yearNewMoney: 5, ssStart: 12, ssAmount: 30_000) };
            // Early-retirement liquidity trap: locked Tax Deferred, small accessible buckets
            yield return new object[] { 4, Outcome.Any, Scenario(45, 55_000, WithdrawalStrategy.TaxOptimized, conversions: false, taxable: 2_000_000, rothBasis: 20_000, brokerageBasis: 80_000, brokerageGain: 40_000) };
            // All fail
            yield return new object[] { 5, Outcome.AllFail, Scenario(70, 400_000, WithdrawalStrategy.ProRata, conversions: true, mean: 0.03, stdDev: 0.05) };
            // All succeed, tax-optimized with conversions (exercises the last-run detail)
            yield return new object[] { 6, Outcome.AllPass, Scenario(68, 30_000, WithdrawalStrategy.TaxOptimized, conversions: true, mean: 0.06, stdDev: 0.02, ssStart: 2, ssAmount: 24_000) };
            // All succeed, pro-rata with SS and NewMoney (exercises the last-run detail)
            yield return new object[] { 7, Outcome.AllPass, Scenario(64, 35_000, WithdrawalStrategy.ProRata, conversions: false, mean: 0.06, stdDev: 0.02, newMoney: 200_000, yearNewMoney: 4, ssStart: 1, ssAmount: 28_000) };
            // SS surplus saved to Brokerage, mostly-gain buckets, age 55 crossing 59.5
            yield return new object[] { 8, Outcome.Any, Scenario(55, 30_000, WithdrawalStrategy.TaxOptimized, conversions: true, rothBasis: 5_000, rothGain: 300_000, brokerageBasis: 20_000, brokerageGain: 400_000, ssStart: 0, ssAmount: 45_000) };
        }

        [Theory]
        [MemberData(nameof(Scenarios))]
        public void CurrentEngine_MatchesLegacyEngine(int seed, Outcome outcome, SimulationParameters parameters)
        {
            var legacyOutput = LegacyMonteCarloEngine.Run(parameters, new Random(seed));
            AssertOutcome(outcome, legacyOutput.Result.OutOfMoneyCount, parameters.Iterations);

            var legacy = Flatten(legacyOutput);
            var current = Flatten(MonteCarloEngine.Run(parameters, new Random(seed)));

            Assert.Equal(legacy.Count, current.Count);
            for (int i = 0; i < legacy.Count; i++)
            {
                Assert.Equal(legacy[i].Path, current[i].Path);
                AssertValueEqual(legacy[i].Path, legacy[i].Value, current[i].Value);
            }
        }

        // Pins each scenario's pass/fail mix, so e.g. the all-pass scenarios keep exercising the last-run detail.
        public enum Outcome { Any, AllPass, AllFail, Mixed }

        private static void AssertOutcome(Outcome outcome, double failures, int iterations)
        {
            switch (outcome)
            {
                case Outcome.AllPass: Assert.Equal(0, failures); break;
                case Outcome.AllFail: Assert.Equal(iterations, failures); break;
                case Outcome.Mixed: Assert.InRange(failures, 1, iterations - 1); break;
            }
        }

        private record Entry(string Path, object? Value);

        private static readonly string[] YearFields =
        {
            "Year", "RateOfReturn", "ReturnAmount", "Withdrawal", "TaxableWithdrawal", "BrokerageWithdrawal",
            "RothWithdrawal", "TaxRate", "Balance", "TaxableBalance", "BrokerageBalance", "RothBalance",
            "OrdinaryTaxAmount", "CapitalGainsTaxAmount", "OrdinaryBracketRate", "AmountUntilNextBracket",
            "NextBracketRate", "AgeEligible", "RothConversionAmount", "RothConversionTax", "AgeInYear",
            "TaxableWithdrawalPercentOfBalance", "SocialSecurityIncome", "SocialSecurityTax"
        };

        // The fields the pre-refactor Last* lists carried, keyed by their RunYearDetail property name.
        private static readonly (string Field, string LegacyList)[] LastRunFields =
        {
            ("Balance", "LastBalances"), ("ReturnAmount", "LastAnnualReturns"), ("Withdrawal", "LastAnnualWithdrawals"),
            ("TaxableBalance", "LastTaxableBalances"), ("BrokerageBalance", "LastBrokerageBalances"), ("RothBalance", "LastRothBalances"),
            ("TaxableWithdrawal", "LastTaxableWithdrawals"), ("BrokerageWithdrawal", "LastBrokerageWithdrawals"), ("RothWithdrawal", "LastRothWithdrawals"),
            ("TaxRate", "LastTaxRates"), ("OrdinaryTaxAmount", "LastOrdinaryTaxAmounts"), ("CapitalGainsTaxAmount", "LastCapitalGainsTaxAmounts"),
            ("OrdinaryBracketRate", "LastOrdinaryBracketRates"), ("AmountUntilNextBracket", "LastAmountsUntilNextBracket"), ("NextBracketRate", "LastNextBracketRates"),
            ("RothConversionAmount", "LastRothConversionAmounts"), ("RothConversionTax", "LastRothConversionTaxes"), ("AgeInYear", "LastAgesInYear"),
            ("TaxableWithdrawalPercentOfBalance", "LastTaxableWithdrawalPercents"), ("SocialSecurityIncome", "LastSocialSecurityIncomes"),
            ("SocialSecurityTax", "LastSocialSecurityTaxes")
        };

        private static object? Prop(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target);

        private static void AddYear(List<Entry> into, string prefix, object yearDetail)
        {
            foreach (var field in YearFields)
                into.Add(new($"{prefix}.{field}", Prop(yearDetail, field)));
        }

        private static void AddList(List<Entry> into, string name, IEnumerable values)
        {
            int i = 0;
            foreach (var v in values) into.Add(new($"{name}[{i++}]", v));
            into.Add(new($"{name}.Count", i));
        }

        private static void AddLastRun(List<Entry> into, int count, Func<string, string, int, object?> valueAt)
        {
            into.Add(new("LastRun.Count", count));
            for (int j = 0; j < count; j++)
                foreach (var (field, legacyList) in LastRunFields)
                    into.Add(new($"LastRun[{j}].{field}", valueAt(field, legacyList, j)));
        }

        private static List<Entry> Flatten(Legacy.SimulationRunOutput output)
        {
            var e = new List<Entry>();
            var r = output.Result;
            AddList(e, "AllRates", output.AllRates);
            e.Add(new("OutOfMoneyMessage", output.OutOfMoneyMessage));
            e.Add(new("OutOfMoneyCount", r.OutOfMoneyCount));
            AddList(e, "YearsOutOfMoney", r.YearsOutOfMoney);
            AddList(e, "FailedScenarioAverages", r.FailedScenarioAverages);
            AddList(e, "SuccessMoneyRemaining", r.SuccessMoneyRemaining);

            e.Add(new("Runs.Count", r.EndingBalances.Count));
            for (int i = 0; i < r.EndingBalances.Count; i++)
            {
                string p = $"Runs[{i}]";
                e.Add(new($"{p}.EndingBalance", r.EndingBalances[i]));
                e.Add(new($"{p}.AverageAnnualReturn", r.AverageAnnualReturns[i]));
                e.Add(new($"{p}.AverageTaxRate", r.AverageTaxRates[i]));
                e.Add(new($"{p}.LifetimeTaxesPaid", r.LifetimeTaxesPaid[i]));
                e.Add(new($"{p}.FailureYear", r.FailureYears[i]));
                e.Add(new($"{p}.HighestReturnYear", r.HighestReturnYears[i]));
                e.Add(new($"{p}.HighestReturnValue", r.HighestReturnValues[i]));
                e.Add(new($"{p}.LowestReturnYear", r.LowestReturnYears[i]));
                e.Add(new($"{p}.LowestReturnValue", r.LowestReturnValues[i]));
                e.Add(new($"{p}.LowestBalanceYear", r.LowestBalanceYears[i]));
                e.Add(new($"{p}.LowestBalanceValue", r.LowestBalanceValues[i]));
                e.Add(new($"{p}.Years.Count", r.RunDetails[i].Count));
                for (int y = 0; y < r.RunDetails[i].Count; y++)
                    AddYear(e, $"{p}.Years[{y}]", r.RunDetails[i][y]);
            }

            // The last-run detail is only ever displayed when every run succeeded.
            if (r.OutOfMoneyCount == 0)
                AddLastRun(e, output.LastBalances!.Count, (_, list, j) => ((IList)Prop(output, list)!)[j]);
            return e;
        }

        private static List<Entry> Flatten(Core.SimulationRunOutput output)
        {
            var e = new List<Entry>();
            var r = output.Result;
            AddList(e, "AllRates", output.AllRates);
            e.Add(new("OutOfMoneyMessage", output.OutOfMoneyMessage));
            e.Add(new("OutOfMoneyCount", r.OutOfMoneyCount));
            AddList(e, "YearsOutOfMoney", r.YearsOutOfMoney);
            AddList(e, "FailedScenarioAverages", r.FailedScenarioAverages);
            AddList(e, "SuccessMoneyRemaining", r.SuccessMoneyRemaining);

            e.Add(new("Runs.Count", r.EndingBalances.Count));
            for (int i = 0; i < r.EndingBalances.Count; i++)
            {
                string p = $"Runs[{i}]";
                e.Add(new($"{p}.EndingBalance", r.EndingBalances[i]));
                e.Add(new($"{p}.AverageAnnualReturn", r.AverageAnnualReturns[i]));
                e.Add(new($"{p}.AverageTaxRate", r.AverageTaxRates[i]));
                e.Add(new($"{p}.LifetimeTaxesPaid", r.LifetimeTaxesPaid[i]));
                e.Add(new($"{p}.FailureYear", r.FailureYears[i]));
                e.Add(new($"{p}.HighestReturnYear", r.HighestReturnYears[i]));
                e.Add(new($"{p}.HighestReturnValue", r.HighestReturnValues[i]));
                e.Add(new($"{p}.LowestReturnYear", r.LowestReturnYears[i]));
                e.Add(new($"{p}.LowestReturnValue", r.LowestReturnValues[i]));
                e.Add(new($"{p}.LowestBalanceYear", r.LowestBalanceYears[i]));
                e.Add(new($"{p}.LowestBalanceValue", r.LowestBalanceValues[i]));
                e.Add(new($"{p}.Years.Count", r.RunDetails[i].Count));
                for (int y = 0; y < r.RunDetails[i].Count; y++)
                    AddYear(e, $"{p}.Years[{y}]", r.RunDetails[i][y]);
            }

            if (r.OutOfMoneyCount == 0)
                AddLastRun(e, output.LastBalances!.Count, (_, list, j) => ((IList)Prop(output, list)!)[j]);
            return e;
        }

        private static void AssertValueEqual(string path, object? expected, object? actual)
        {
            if (expected is null || actual is null || expected is string || actual is string)
            {
                Assert.True(Equals(expected, actual), $"{path}: expected <{expected}> but was <{actual}>");
                return;
            }

            double a = Convert.ToDouble(expected), b = Convert.ToDouble(actual);
            if (a == b) return;
            double tolerance = 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
            Assert.True(Math.Abs(a - b) <= tolerance, $"{path}: expected {a:R} but was {b:R}");
        }
    }
}
