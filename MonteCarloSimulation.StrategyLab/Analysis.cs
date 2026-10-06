using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // Turns the per-(scenario, combination) rows into the report's numbers. Every comparison is paired: the same
    // scenario and market paths, only the combination changes. The main figure is each combination's Spend825 as
    // a % difference from the baseline's (today's default, W2+C1) on the same scenario.
    internal static class Analysis
    {
        // Differences within +/-0.5% count as ties: break-evens are found to $100, so smaller gaps are noise.
        public const double TieBand = 0.005;
        // Scenarios the baseline can't fund at $1,000 a year are left out of % comparisons (nothing to scale by).
        public const double MinimumBaselineSpend = 1_000;
        private const int BootstrapResamples = 2_000;

        public static Summary Summarize(
            CandidateSet set, IReadOnlyList<Scenario> scenarios, IReadOnlyList<ResultRow> rows, IReadOnlyList<SingleYearCase> singleYear,
            IReadOnlyList<(Scenario Scenario, IReadOnlyList<ResultRow> Rows)> pageDefault, CaseStudy? caseStudy, RunConfig config)
        {
            var byScenario = rows.GroupBy(r => r.ScenarioId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Combination));
            var baselineCode = set.Baseline.Code;
            var included = scenarios
                .Where(s => byScenario.TryGetValue(s.Id, out var r) && r.Count == set.All.Count
                            && r[baselineCode].Spend825 >= MinimumBaselineSpend)
                .ToList();
            var rng = new Random(config.Seed);

            double Diff(Scenario s, string code, Func<ResultRow, double> metric)
            {
                double b = metric(byScenario[s.Id][baselineCode]);
                return b > 0 ? metric(byScenario[s.Id][code]) / b - 1 : 0;
            }

            var vsBaseline = set.All.Select(c => Compare(
                c.Code, c.Order.Name + " / " + c.Policy.Name,
                included.Select(s => Diff(s, c.Code, r => r.Spend825)).ToArray(),
                included.Select(s => Diff(s, c.Code, r => r.Spend50)).ToArray(),
                included.Select(s => Diff(s, c.Code, r => r.MedianTaxesAtCommon)).ToArray(),
                included.Select(s => Diff(s, c.Code, r => r.MedianAfterTaxWealthAtCommon)).ToArray(),
                included.Select(s => byScenario[s.Id][c.Code].SurvivalAtCommon).ToArray(),
                rng)).ToList();

            // The user's direct question: Pro-rata against Tax-optimized, with conversions on and off.
            Comparison Pair(string label, string code, string against) =>
                Compare(code + " vs " + against, label,
                    included.Select(s => Ratio(byScenario[s.Id][code].Spend825, byScenario[s.Id][against].Spend825)).ToArray(),
                    included.Select(s => Ratio(byScenario[s.Id][code].Spend50, byScenario[s.Id][against].Spend50)).ToArray(),
                    [], [], [], rng);
            var codes = set.All.Select(c => c.Code).ToHashSet();
            var direct = new (string Label, string Code, string Against)[]
                {
                    ("Pro-rata vs Tax-optimized, app conversions on", "W1+C1", "W2+C1"),
                    ("Pro-rata vs Tax-optimized, no conversions", "W1+C0", "W2+C0"),
                    ("Tax-optimized: app conversions vs none", "W2+C1", "W2+C0"),
                    ("Pro-rata: app conversions vs none", "W1+C1", "W1+C0"),
                    ("Tax-optimized: 0% harvesting vs none", "W2+C1", "W2nh+C1"),
                }
                .Where(p => codes.Contains(p.Code) && codes.Contains(p.Against))
                .Select(p => Pair(p.Label, p.Code, p.Against))
                .ToList();

            // Regret: how much more the best of all combinations spends than the baseline, per scenario.
            var regrets = included.Select(s =>
            {
                var best = set.All.Select(c => byScenario[s.Id][c.Code]).MaxBy(r => r.Spend825)!;
                return (Scenario: s, Best: best, Regret: best.Spend825 / byScenario[s.Id][baselineCode].Spend825 - 1);
            }).ToList();
            var regretValues = regrets.Select(r => r.Regret).ToArray();
            Dictionary<string, int> BestCounts(double over) => regrets.Where(r => r.Regret > over)
                .GroupBy(r => r.Best.Combination).OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());

            var regret = new RegretSummary(
                Mean(regretValues), Percentile(regretValues, 0.5), Percentile(regretValues, 0.9), Percentile(regretValues, 0.95),
                regretValues.DefaultIfEmpty().Max(),
                Share(regretValues, v => v > 0.01), Share(regretValues, v => v > 0.03), Share(regretValues, v => v > TieBand),
                BestCounts(TieBand), BestCounts(0.03));

            var topRegret = regrets.OrderByDescending(r => r.Regret).Take(10).Select(r =>
            {
                var b = byScenario[r.Scenario.Id][baselineCode];
                return new RegretCase(Describe(r.Scenario), r.Scenario.RetirementAge, r.Scenario.TaxDeferredShare,
                    b.Spend825, r.Best.Combination, r.Best.Spend825, r.Regret,
                    b.MedianTaxesAtCommon, r.Best.MedianTaxesAtCommon, b.MedianAfterTaxWealthAtCommon, r.Best.MedianAfterTaxWealthAtCommon);
            }).ToList();

            var slices = Slices(set, included, byScenario);

            // The gap to the best combination for the household types where the baseline does well and badly.
            // Pro-rata column: Pro-rata + app conversions vs the baseline, the user's direct question.
            string proRataCode = set.ProRataCode;
            SubsetResult Subset(string label, Func<Scenario, bool> member)
            {
                var gaps = regrets.Where(r => member(r.Scenario)).Select(r => r.Regret).ToArray();
                var proRata = included.Where(member)
                    .Select(s => byScenario[s.Id][proRataCode].Spend825 / byScenario[s.Id][baselineCode].Spend825 - 1).ToArray();
                return new SubsetResult(label, gaps.Length, Percentile(gaps, 0.5), Mean(gaps), Percentile(gaps, 0.9),
                    Share(gaps, v => v > 0.01), Share(gaps, v => v > 0.03), Percentile(proRata, 0.5));
            }
            var subsets = new List<SubsetResult>
            {
                Subset("Everyone", _ => true),
                Subset("Retire at 59\u00bd or later", s => !s.RetiresBefore59Half),
                Subset("Retire before 59\u00bd, under 75% Tax Deferred", s => s.RetiresBefore59Half && s.TaxDeferredShare < 0.75),
                Subset("Retire before 59\u00bd, 75%+ Tax Deferred", s => s.RetiresBefore59Half && s.TaxDeferredShare >= 0.75),
            };

            var excludedCount = scenarios.Count(s => byScenario.ContainsKey(s.Id)) - included.Count;

            return new Summary(
                config,
                set.Name,
                set.All.Select(c => new CombinationInfo(c.Code, c.Order.Code, c.Order.Name, c.Policy.Code, c.Policy.Name, c.Funding?.Code)).ToList(),
                Candidates.Definitions().ToList(),
                baselineCode,
                included.Count,
                excludedCount,
                vsBaseline,
                direct,
                regret,
                subsets,
                topRegret,
                slices,
                SummarizeSingleYear(singleYear),
                pageDefault.Select(p => new PageDefaultResult(p.Scenario.InvestmentScenarioId, p.Scenario.Parameters.ScenarioDescription, p.Rows)).ToList(),
                caseStudy,
                null,
                OrderChoices(included, byScenario, baselineCode, codes),
                null,
                ConversionChoices(set, included, byScenario, baselineCode, codes));
        }

        // How the app picks how far to convert, together with the order: each per-household pick against the best of
        // every combination in the set, how often each target wins, whether the lab-only C5 line (stop below the
        // first IRMAA tier) earns a place among the app's targets, and how closely the main page's own picker
        // (survival at one spend on 200 paths) matches the ideal pick. Null for a set without the app's candidates.
        private static ConversionChoice? ConversionChoices(
            CandidateSet set, List<Scenario> included, Dictionary<int, Dictionary<string, ResultRow>> byScenario, string baselineCode, HashSet<string> codes)
        {
            string Code(WithdrawalStrategy order, RothConversionTarget target) =>
                $"{Candidates.AppOrders[order].Code}+{Candidates.AppTargets[target].Code}";
            var appPairs = AutomaticStrategy.Orders.SelectMany(o => AutomaticStrategy.Targets.Select(t => (Order: o, Target: t))).ToList();
            var appCodes = appPairs.Select(p => Code(p.Order, p.Target)).ToList();
            var todayCodes = AutomaticStrategy.Orders.Select(o => Code(o, RothConversionTarget.Bracket12)).ToList();
            var belowIrmaaCodes = AutomaticStrategy.Orders.Select(o => $"{Candidates.AppOrders[o].Code}+{Candidates.BelowIrmaa.Code}").ToList();
            if (!appCodes.Concat(belowIrmaaCodes).All(codes.Contains) || included.Count == 0) return null;

            double Spend(Scenario s, string code) => byScenario[s.Id][code].Spend825;
            double Pick(Scenario s, IEnumerable<string> pickCodes) => pickCodes.Max(c => Spend(s, c));
            var bestOfAll = included.ToDictionary(s => s.Id, s => set.All.Max(c => Spend(s, c.Code)));
            var bins = Dimensions(baselineCode, byScenario)
                .SelectMany(d => d.Order.Select(label => (Label: $"{d.Name}: {label}", Members: included.Where(s => d.Bin(s) == label).ToList())))
                .Where(b => b.Members.Count > 0)
                .ToList();

            OrderChoice Measure(string code, string name, Func<Scenario, double> spend)
            {
                double Shortfall(Scenario s) => spend(s) / bestOfAll[s.Id] - 1;
                var shortfalls = included.Select(Shortfall).ToArray();
                var worst = bins.Select(b => (b.Label, Mean: b.Members.Average(Shortfall))).MinBy(b => b.Mean);
                double within = Share(shortfalls, v => v >= -TieBand);
                return new OrderChoice(code, name, within, Mean(shortfalls), worst.Label, worst.Mean,
                    Qualifies: within >= 0.95 && worst.Mean >= -0.01);
            }

            var withBelowIrmaa = appCodes.Concat(belowIrmaaCodes).ToList();
            var rows = new List<OrderChoice>
            {
                Measure("W1/W2+C1", "Pick of W1 or W2 at the 12% line (before this change)", s => Pick(s, todayCodes)),
                Measure("W1/W2+C0/C1/C3/C4", "Pick of order and conversion line (app)", s => Pick(s, appCodes)),
                Measure("W1/W2+C0/C1/C3/C4/C5", "The same, plus C5 below the first IRMAA tier", s => Pick(s, withBelowIrmaa)),
            };

            // Which target the app's pick lands on (ties to the first pair in AutomaticStrategy's order)
            var targetShares = AutomaticStrategy.Targets.ToDictionary(t => t.ToString(), t => 0.0);
            foreach (var s in included)
            {
                var winner = appPairs.MaxBy(p => Spend(s, Code(p.Order, p.Target)));
                targetShares[winner.Target.ToString()] += 1.0 / included.Count;
            }

            var gainsFromBelowIrmaa = included.Select(s => Pick(s, withBelowIrmaa) / Pick(s, appCodes) - 1).ToArray();
            double belowIrmaaBestShare = Share(gainsFromBelowIrmaa, v => v > 0);
            double belowIrmaaAddedMean = Mean(gainsFromBelowIrmaa);

            return new ConversionChoice(rows, targetShares, belowIrmaaBestShare, belowIrmaaAddedMean,
                BelowIrmaaAdopted: belowIrmaaBestShare >= 0.03 && belowIrmaaAddedMean >= 0.001,
                Picker: MainPagePicker(included, byScenario, s => Pick(s, appCodes), Code));
        }

        // The main page's picker (AutomaticStrategy.Resolve: survival at the entered spend on 200 paths) run on every
        // household at its ideal pick's 82.5% spend, and the shortfall of the pair it chooses against that ideal.
        private static PickerAgreement MainPagePicker(
            List<Scenario> included, Dictionary<int, Dictionary<string, ResultRow>> byScenario, Func<Scenario, double> idealSpend,
            Func<WithdrawalStrategy, RothConversionTarget, string> code)
        {
            var shortfalls = new double[included.Count];
            var chosen = new RothConversionTarget[included.Count];
            Parallel.For(0, included.Count, i =>
            {
                var s = included[i];
                var p = Scenario.Copy(s.Parameters);
                p.Withdrawal = idealSpend(s);
                p.EnableRothConversions = true;
                p.WithdrawalStrategy = WithdrawalStrategy.Automatic;
                p.RothConversionTarget = RothConversionTarget.Automatic;
                var (order, target) = AutomaticStrategy.Resolve(p, RetirementTimeline.Build(p));
                chosen[i] = target;
                shortfalls[i] = byScenario[s.Id][code(order, target)].Spend825 / idealSpend(s) - 1;
            });
            var targetShares = AutomaticStrategy.Targets.ToDictionary(t => t.ToString(), t => Share(chosen.Select(c => c == t ? 1.0 : 0).ToArray(), v => v > 0));
            return new PickerAgreement(Share(shortfalls, v => v >= -TieBand), Mean(shortfalls), Percentile(shortfalls, 0.05), targetShares);
        }

        private static double Ratio(double value, double against) => against > 0 ? value / against - 1 : 0;

        private static Comparison Compare(
            string code, string label, double[] spend825, double[] spend50, double[] taxes, double[] wealth, double[] survival, Random rng) =>
            new(code, label, spend825.Length,
                Mean(spend825), Percentile(spend825, 0.5), Percentile(spend825, 0.05), Percentile(spend825, 0.95),
                Share(spend825, v => v > TieBand), Share(spend825, v => v < -TieBand),
                Bootstrap(spend825, Mean, rng), Bootstrap(spend825, v => Percentile(v, 0.5), rng),
                Percentile(spend50, 0.5), Mean(spend50),
                taxes.Length > 0 ? Percentile(taxes, 0.5) : null,
                wealth.Length > 0 ? Percentile(wealth, 0.5) : null,
                survival.Length > 0 ? Mean(survival) : null);

        // Median % difference vs the baseline within each slice of the scenarios, per combination.
        private static List<Slice> Slices(CandidateSet set, List<Scenario> included, Dictionary<int, Dictionary<string, ResultRow>> byScenario)
        {
            string baselineCode = set.Baseline.Code;
            return Dimensions(baselineCode, byScenario).Select(d => new Slice(d.Name, d.Order.Select(label =>
            {
                var members = included.Where(s => d.Bin(s) == label).ToList();
                var medians = set.All.ToDictionary(c => c.Code, c => members.Count == 0 ? 0 :
                    Percentile(members.Select(s => byScenario[s.Id][c.Code].Spend825 / byScenario[s.Id][baselineCode].Spend825 - 1).ToArray(), 0.5));
                var means = set.All.ToDictionary(c => c.Code, c => members.Count == 0 ? 0 :
                    Mean(members.Select(s => byScenario[s.Id][c.Code].Spend825 / byScenario[s.Id][baselineCode].Spend825 - 1).ToArray()));
                return new SliceBin(label, members.Count, medians, means);
            }).ToList())).ToList();
        }

        // The household types results are sliced by.
        private static (string Name, Func<Scenario, string> Bin, string[] Order)[] Dimensions(
            string baselineCode, Dictionary<int, Dictionary<string, ResultRow>> byScenario)
        {
            double SpendToAssets(Scenario s) => byScenario[s.Id][baselineCode].Spend825 / s.TotalAssets;

            return new (string Name, Func<Scenario, string> Bin, string[] Order)[]
            {
                ("Tax Deferred share", s => Bin(s.TaxDeferredShare, [0.25, 0.5, 0.75], ["0-25%", "25-50%", "50-75%", "75-100%"]),
                    ["0-25%", "25-50%", "50-75%", "75-100%"]),
                ("Brokerage gain fraction", s => Bin(s.BrokerageGainFraction, [0.3, 0.6], ["0-30%", "30-60%", "60-90%"]),
                    ["0-30%", "30-60%", "60-90%"]),
                ("Retires before 59½", s => s.RetiresBefore59Half ? "Yes" : "No", ["Yes", "No"]),
                ("Social Security (monthly, today's $)", s => Bin(s.Parameters.SocialSecurityMonthlyAmount, [1, 2_000, 3_500], ["None", "Under $2,000", "$2,000-3,500", "Over $3,500"]),
                    ["None", "Under $2,000", "$2,000-3,500", "Over $3,500"]),
                ("Spend-to-assets (baseline 82.5% spend)", s => Bin(SpendToAssets(s), [0.03, 0.045, 0.06], ["Under 3%", "3-4.5%", "4.5-6%", "Over 6%"]),
                    ["Under 3%", "3-4.5%", "4.5-6%", "Over 6%"]),
                ("Total assets", s => Bin(s.TotalAssets, [750_000, 1_500_000, 3_000_000], ["Under $750k", "$750k-1.5M", "$1.5-3M", "Over $3M"]),
                    ["Under $750k", "$750k-1.5M", "$1.5-3M", "Over $3M"]),
                ("Investment scenario", s => $"Scenario {s.InvestmentScenarioId}", ["Scenario 1", "Scenario 2", "Scenario 3", "Scenario 4"]),
            };
        }

        // How the app picks its withdrawal order: under the app's conversion policy (C1), each order's 82.5% spend
        // against the best order for the same household. An order qualifies as the one fixed rule when it's within
        // the tie band of the best in at least 95% of households and no household type falls short by more than 1%
        // on average. Null for a set without every order under C1.
        private static List<OrderChoice>? OrderChoices(
            List<Scenario> included, Dictionary<int, Dictionary<string, ResultRow>> byScenario, string baselineCode, HashSet<string> codes)
        {
            var orderCodes = Candidates.Orders.Select(o => $"{o.Code}+{Candidates.AppDefault.Code}").ToList();
            if (!orderCodes.All(codes.Contains) || included.Count == 0) return null;

            var best = included.ToDictionary(s => s.Id, s => orderCodes.Max(c => byScenario[s.Id][c].Spend825));
            var bins = Dimensions(baselineCode, byScenario)
                .SelectMany(d => d.Order.Select(label => (Label: $"{d.Name}: {label}", Members: included.Where(s => d.Bin(s) == label).ToList())))
                .Where(b => b.Members.Count > 0)
                .ToList();

            OrderChoice Measure(string code, string name, Func<Scenario, double> spend)
            {
                double Shortfall(Scenario s) => spend(s) / best[s.Id] - 1;
                var shortfalls = included.Select(Shortfall).ToArray();
                var worst = bins.Select(b => (b.Label, Mean: b.Members.Average(Shortfall))).MinBy(b => b.Mean);
                double within = Share(shortfalls, v => v >= -TieBand);
                return new OrderChoice(code, name, within, Mean(shortfalls), worst.Label, worst.Mean,
                    Qualifies: within >= 0.95 && worst.Mean >= -0.01);
            }

            // The app's Automatic order: Core's two orders, the better one per household (as the Optimal page picks)
            string proRata = $"{Candidates.ProRata.Code}+{Candidates.AppDefault.Code}";
            string taxOptimized = $"{Candidates.TaxOptimized.Code}+{Candidates.AppDefault.Code}";
            return Candidates.Orders
                .Select(o => Measure($"{o.Code}+{Candidates.AppDefault.Code}", o.Name, s => byScenario[s.Id][$"{o.Code}+{Candidates.AppDefault.Code}"].Spend825))
                .Append(Measure($"{Candidates.ProRata.Code}/{Candidates.TaxOptimized.Code}+{Candidates.AppDefault.Code}", "Per-household pick of W1 or W2",
                    s => Math.Max(byScenario[s.Id][proRata].Spend825, byScenario[s.Id][taxOptimized].Spend825)))
                .ToList();
        }

        private static string Bin(double value, double[] edges, string[] labels)
        {
            for (int i = 0; i < edges.Length; i++)
                if (value < edges[i]) return labels[i];
            return labels[^1];
        }

        private static SingleYearSummary SummarizeSingleYear(IReadOnlyList<SingleYearCase> cases)
        {
            var checkable = cases.Where(c => !c.Shortfall).ToList();
            var gaps = checkable.Select(c => c.Gap).ToArray();
            bool Material(SingleYearCase c) => c.Gap > 5;
            var material = checkable.Where(Material).ToList();

            // Which way the plan differed from the one-year minimum
            var moreTaxDeferred = material.Where(c => c.PlanGrossTaxable > c.BestGrossTaxable + 1).ToList();
            var lessTaxDeferred = material.Where(c => c.PlanGrossTaxable < c.BestGrossTaxable - 1).ToList();

            return new SingleYearSummary(
                cases.Count, cases.Count(c => c.Shortfall), checkable.Count,
                checkable.Count(c => c.Gap <= 5), material.Count,
                moreTaxDeferred.Count, lessTaxDeferred.Count,
                gaps.Length > 0 ? Mean(gaps) : 0,
                material.Count > 0 ? Percentile(material.Select(c => c.Gap).ToArray(), 0.5) : 0,
                gaps.DefaultIfEmpty().Max(),
                material.Count > 0 ? Percentile(material.Select(c => c.Gap / c.Need).ToArray(), 0.5) : 0,
                checkable.Count(c => c.Gap < -5),
                cases.Count(c => c.RothBeforeOthers),
                material.OrderByDescending(c => c.Gap).Take(10).ToList(),
                BinGaps(material, c => c.GainFraction, [0.2, 0.4, 0.6, 0.8], ["0-20%", "20-40%", "40-60%", "60-80%", "80-100%"], checkable));
        }

        private static List<GapBin> BinGaps(List<SingleYearCase> material, Func<SingleYearCase, double> key, double[] edges, string[] labels, List<SingleYearCase> all) =>
            labels.Select(label => new GapBin(
                label,
                all.Count(c => Bin(key(c), edges, labels) == label),
                material.Count(c => Bin(key(c), edges, labels) == label),
                material.Where(c => Bin(key(c), edges, labels) == label).Select(c => c.Gap).DefaultIfEmpty().Average())).ToList();

        public static string Describe(Scenario s)
        {
            var p = s.Parameters;
            string ss = p.SocialSecurityMonthlyAmount > 0 ? $"SS ${p.SocialSecurityMonthlyAmount:N0}/mo at {s.SocialSecurityStartAge}" : "no SS";
            string inheritance = p.NewMoney > 0 ? $", inherits ${p.NewMoney / 1000:N0}k in yr {p.YearNewMoney}" : "";
            return $"#{s.Id}: retire {p.RetirementDate:yyyy} at {s.RetirementAge:0.0} for {p.Years} yrs; ${s.TotalAssets / 1000:N0}k " +
                   $"({s.TaxDeferredShare:P0} TD, {s.RothShare:P0} Roth, {1 - s.TaxDeferredShare - s.RothShare:P0} brokerage @ {s.BrokerageGainFraction:P0} gains); " +
                   $"{ss}{inheritance}; inv. scenario {s.InvestmentScenarioId}";
        }

        private static double Mean(double[] v) => v.Length == 0 ? 0 : v.Average();

        private static double Share(double[] v, Func<double, bool> test) => v.Length == 0 ? 0 : v.Count(test) / (double)v.Length;

        // Linear-interpolated percentile.
        public static double Percentile(double[] values, double p)
        {
            if (values.Length == 0) return 0;
            var sorted = values.OrderBy(v => v).ToArray();
            double position = p * (sorted.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        }

        // 95% bootstrap interval (percentile method), resampling scenarios.
        private static double[] Bootstrap(double[] values, Func<double[], double> statistic, Random rng)
        {
            if (values.Length == 0) return [0, 0];
            var stats = new double[BootstrapResamples];
            var sample = new double[values.Length];
            for (int b = 0; b < BootstrapResamples; b++)
            {
                for (int i = 0; i < sample.Length; i++) sample[i] = values[rng.Next(values.Length)];
                stats[b] = statistic(sample);
            }
            return [Percentile(stats, 0.025), Percentile(stats, 0.975)];
        }
    }

    internal sealed record RunConfig(int Scenarios, int Paths, int Seed, int SingleYearCases, DateTime GeneratedAtUtc, double RuntimeSeconds);

    internal sealed record CombinationInfo(string Code, string Order, string OrderName, string Policy, string PolicyName, string? Funding);

    // The conversion-tax funding comparison (the lab's "funding" set), carried in the main summary for the Model
    // Info page: every funding rule against today's W2+C1 with Brokerage-only funding.
    internal sealed record FundingComparison(string Baseline, int ScenariosCompared, List<Comparison> VsBaseline, List<SubsetResult> Subsets, List<Slice> Slices);

    // Differences are fractions (0.012 = +1.2%). Taxes/Wealth/Survival are at the common (baseline) spend.
    internal sealed record Comparison(
        string Code, string Label, int Scenarios,
        double MeanSpendDiff, double MedianSpendDiff, double P5SpendDiff, double P95SpendDiff,
        double WinRate, double LossRate, double[] MeanCi, double[] MedianCi,
        double MedianSpend50Diff, double MeanSpend50Diff,
        double? MedianTaxDiffAtCommon, double? MedianWealthDiffAtCommon, double? MeanSurvivalAtCommon);

    internal sealed record RegretSummary(
        double Mean, double Median, double P90, double P95, double Max,
        double ShareOver1Pct, double ShareOver3Pct, double ShareOverTieBand,
        Dictionary<string, int> BestCombinationCounts, Dictionary<string, int> BestCombinationCountsOver3Pct);

    // The gap to the best combination within one household type. ProRataMedianDiff: Pro-rata + app conversions
    // vs the baseline, median.
    internal sealed record SubsetResult(
        string Label, int Scenarios, double MedianGap, double MeanGap, double P90Gap, double ShareOver1Pct, double ShareOver3Pct,
        double ProRataMedianDiff);

    internal sealed record RegretCase(
        string Scenario, double RetirementAge, double TaxDeferredShare, double BaselineSpend825, string BestCombination, double BestSpend825, double Regret,
        double BaselineMedianTaxes, double BestMedianTaxes, double BaselineMedianWealth, double BestMedianWealth);

    internal sealed record Slice(string Dimension, List<SliceBin> Bins);

    internal sealed record SliceBin(string Label, int Scenarios, Dictionary<string, double> MedianDiff, Dictionary<string, double> MeanDiff);

    internal sealed record SingleYearSummary(
        int Cases, int Shortfalls, int Checked, int WithinFiveDollars, int MaterialGaps,
        int PlanUsedMoreTaxDeferred, int PlanUsedLessTaxDeferred,
        double MeanGap, double MedianMaterialGap, double MaxGap, double MedianMaterialGapShareOfNeed,
        int PlanBeatBruteForce, int RothBeforeOthers,
        List<SingleYearCase> LargestGaps, List<GapBin> ByGainFraction);

    internal sealed record GapBin(string Label, int Cases, int MaterialGaps, double MeanMaterialGap);

    internal sealed record PageDefaultResult(int InvestmentScenarioId, string Description, IReadOnlyList<ResultRow> Rows);

    internal sealed record Summary(
        RunConfig Config,
        string Set,
        List<CombinationInfo> Combinations,
        List<StrategyDefinition> Definitions,
        string Baseline,
        int ScenariosCompared,
        int ScenariosExcluded,
        List<Comparison> VsBaseline,
        List<Comparison> DirectQuestion,
        RegretSummary Regret,
        List<SubsetResult> Subsets,
        List<RegretCase> TopRegret,
        List<Slice> Slices,
        SingleYearSummary SingleYear,
        List<PageDefaultResult> PageDefault,
        CaseStudy? CaseStudy,
        FundingComparison? Funding,
        List<OrderChoice>? OrderChoice,
        List<OptimalDefaultsRow>? OptimalDefaults,
        ConversionChoice? ConversionChoice);

    // The app's pick of order and conversion line, against the best of every combination per household (Rows use
    // OrderChoice's shape, with "best" meaning best of all). TargetShares: how often each target wins the ideal
    // pick. BelowIrmaa*: whether the lab-only C5 line earns a place (best somewhere in >=3% of households and >=0.1%
    // added on average). Picker: the main page's own picker against the ideal pick.
    internal sealed record ConversionChoice(
        List<OrderChoice> Rows, Dictionary<string, double> TargetShares,
        double BelowIrmaaBestShare, double BelowIrmaaAddedMean, bool BelowIrmaaAdopted, PickerAgreement Picker);

    // Share of households where the main page's picker lands within the tie band of the ideal pick, its average and
    // 5th-percentile shortfall, and how often it chooses each target.
    internal sealed record PickerAgreement(double ShareWithinTieBand, double MeanShortfall, double P5Shortfall, Dictionary<string, double> TargetShares);

    // One withdrawal order under the app's conversion policy, against the best order per household: the share of
    // households within the tie band of the best, the average shortfall, and the household type where it falls
    // shortest on average. Qualifies: it meets the rule for being the app's one fixed order.
    internal sealed record OrderChoice(
        string Code, string Name, double ShareWithinTieBand, double MeanShortfall, string WorstSlice, double WorstSliceMeanShortfall, bool Qualifies);
}
