using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // The schedule study: would Roth conversion lines that change from year to year (ConversionSchedules) let
    // households spend more at 82.5% survival than today's app, which picks one withdrawal order and one line per
    // household (AutomaticStrategy)?
    //
    // It needs two runs of the same households: the fit run (market paths 0..n-1) and the validation run (paths
    // n..2n-1, --validate). Rules are chosen on the fit run's even-numbered households and judged on the odd-numbered
    // ones; and every per-household pick is made on the fit run's markets and measured on the validation run's, the
    // way the app picks on its own seeded markets before a household's real, unknown future. So neither a rule nor a
    // pick is ever scored on the data that chose it. Today's pick is measured the same way, so the comparison is fair.
    internal static class ScheduleStudy
    {
        // The adoption bar, fixed before any results: out of sample, at least +0.5% on average over today's pick,
        // with a 95% interval above zero, and no household type worse than -1% on average.
        public const double AdoptMeanGain = 0.005;
        public const double AdoptWorstSliceMean = -0.01;

        // How many schedules the "add to the app's pick" rules offer, chosen greedily on the fit households.
        public static readonly int[] AddedCounts = [1, 2, 4, 8];

        private const int TreeDepth = 2;
        private const int MinLeafSize = 30;

        // The household facts a decision-tree rule may split on: inputs a household knows, never results.
        private static readonly (string Name, Func<Scenario, double> Value, Func<double, string> Format)[] Features =
        [
            ("retirement age", s => s.RetirementAge, v => $"{v:0.#}"),
            ("Tax Deferred share", s => s.TaxDeferredShare, v => $"{v:P0}"),
            ("total assets", s => s.TotalAssets, v => $"${v / 1000:N0}k"),
            ("monthly Social Security", s => s.Parameters.SocialSecurityMonthlyAmount, v => $"${v:N0}"),
            ("Social Security start age", s => s.SocialSecurityStartAge, v => $"{v:0}"),
            ("years from retirement to RMDs", s => RequiredMinimumDistributions.StartAge(s.Parameters.Birthdate) - s.RetirementAge, v => $"{v:0.#}"),
        ];

        public static ScheduleStudyResult Run(
            CandidateSet set, IReadOnlyList<Scenario> scenarios, IReadOnlyList<ResultRow> fitRows, IReadOnlyList<ResultRow> validationRows, int seed,
            int tradeoffPaths = 0)
        {
            var fit = Index(fitRows);
            var validation = Index(validationRows);
            string baseline = set.Baseline.Code;
            bool Complete(Dictionary<int, Dictionary<string, ResultRow>> rows, Scenario s) =>
                rows.TryGetValue(s.Id, out var r) && r.Count == set.All.Count && r[baseline].Spend825 >= Analysis.MinimumBaselineSpend;
            var included = scenarios.Where(s => Complete(fit, s) && Complete(validation, s)).ToList();
            var train = included.Where(s => s.Id % 2 == 0).ToList();
            var holdout = included.Where(s => s.Id % 2 != 0).ToList();

            // The app's eight pairs first, so a tie keeps today's choice
            var appCodes = Candidates.AppOrders.Values
                .SelectMany(o => Candidates.AppPolicies.Select(p => new Combination(o, p).Code)).ToList();
            var schedules = set.All.Where(c => c.Policy.Schedule is not null).ToList();
            var scheduleCodes = schedules.Select(c => c.Code).ToList();
            var names = set.All.ToDictionary(c => c.Code, c => c.Policy.Name);

            // In sample (choosing rules): the fit run's best of the candidates
            double FitPick(Scenario s, IEnumerable<string> codes) => codes.Max(c => fit[s.Id][c].Spend825);
            // Out of sample (judging rules): chosen on the fit run's markets, measured on the validation run's
            double HonestPick(Scenario s, IEnumerable<string> codes) =>
                validation[s.Id][codes.MaxBy(c => fit[s.Id][c].Spend825)!].Spend825;

            // Each fit household's in-sample gain from offering one more schedule beside the app's eight
            var appFit = train.ToDictionary(s => s.Id, s => FitPick(s, appCodes));
            double AddedGain(Scenario s, string code) => Math.Max(appFit[s.Id], fit[s.Id][code].Spend825) / appFit[s.Id] - 1;

            var rules = new List<Rule>();

            // (a) One schedule for everyone, in place of the per-household pick
            var single = scheduleCodes.MaxBy(c => train.Average(s => fit[s.Id][c].Spend825 / appFit[s.Id] - 1))!;
            rules.Add(new Rule("One schedule for everyone", $"Every household runs {single} ({names[single]}), with no per-household pick.",
                [single], _ => [single]));

            // (b) The app's own method with schedules added to its candidates, picked greedily on the fit households
            var added = new List<string>();
            for (int k = 1; k <= AddedCounts.Max(); k++)
            {
                var next = scheduleCodes.Except(added)
                    .MaxBy(c => train.Average(s => FitPick(s, appCodes.Concat(added).Append(c)) / appFit[s.Id] - 1))!;
                added.Add(next);
                if (AddedCounts.Contains(k))
                {
                    var offered = appCodes.Concat(added).ToList();
                    rules.Add(new Rule($"Today's pick plus {k} schedule{(k == 1 ? "" : "s")}",
                        $"The per-household pick also considers {string.Join(", ", added)}.", added.ToList(), _ => offered));
                }
            }
            var everything = appCodes.Concat(scheduleCodes).ToList();
            rules.Add(new Rule("Today's pick plus every schedule", $"The per-household pick considers all {scheduleCodes.Count} schedules too.",
                scheduleCodes, _ => everything));

            // (c) A shallow decision tree on household facts, one added schedule (or none) per leaf
            var tree = Grow(train, scheduleCodes, AddedGain, TreeDepth);
            rules.Add(new Rule("Decision tree", Describe(tree), Leaves(tree).Where(l => l != null).Select(l => l!).Distinct().ToList(),
                s => Leaf(tree, s) is string code ? appCodes.Append(code).ToList() : appCodes));

            // Judge every rule on the holdout households, against today's pick measured the same way
            var rng = new Random(seed);
            var bins = Analysis.Dimensions(baseline, fit)
                .SelectMany(d => d.Order.Select(label => (Label: $"{d.Name}: {label}", Members: holdout.Where(s => d.Bin(s) == label).ToList())))
                .Where(b => b.Members.Count > 0)
                .ToList();
            var todayHonest = holdout.ToDictionary(s => s.Id, s => HonestPick(s, appCodes));
            var results = rules.Select(rule =>
            {
                double Gain(Scenario s) => HonestPick(s, rule.Candidates(s)) / todayHonest[s.Id] - 1;
                var gains = holdout.Select(Gain).ToArray();
                double inSample = train.Average(s => FitPick(s, rule.Candidates(s)) / appFit[s.Id] - 1);
                var worst = bins.Select(b => (b.Label, Mean: b.Members.Average(Gain))).MinBy(b => b.Mean);
                var ci = Analysis.Bootstrap(gains, Analysis.Mean, rng);
                double mean = Analysis.Mean(gains);
                return new ScheduleRuleResult(rule.Name, rule.Description, rule.Schedules, inSample, mean, ci,
                    Analysis.Percentile(gains, 0.5), Analysis.Share(gains, v => v > Analysis.TieBand), Analysis.Share(gains, v => v < -Analysis.TieBand),
                    worst.Label, worst.Mean,
                    Adopted: mean >= AdoptMeanGain && ci[0] > 0 && worst.Mean >= AdoptWorstSliceMean);
            }).ToList();

            // Where the schedules win at all: in sample, how often each is the fit run's best of everything, and what
            // offering it alone adds on average
            var bestOfAll = train.ToDictionary(s => s.Id, s => everything.MaxBy(c => fit[s.Id][c].Spend825)!);
            var topSchedules = scheduleCodes
                .Select(c => new ScheduleUsage(c, names[c], train.Count(s => bestOfAll[s.Id] == c) / (double)train.Count,
                    train.Average(s => AddedGain(s, c))))
                .OrderByDescending(u => u.MeanAddedGain).Take(15).ToList();
            double scheduleBestShare = train.Count(s => scheduleCodes.Contains(bestOfAll[s.Id])) / (double)train.Count;

            var tradeoffs = tradeoffPaths > 0 ? EarlyConversionTradeoffs(set, included, fit, tradeoffPaths) : [];

            return new ScheduleStudyResult(train.Count, holdout.Count, scheduleCodes.Count, scheduleBestShare, results, topSchedules,
                AdoptMeanGain, AdoptWorstSliceMean, tradeoffs);
        }

        // The "convert heavily early" policies the trade-off table compares with the baseline (the app's 12% fill)
        public static readonly string[] HeavyEarlyCodes = ["W2+C3", "W2+C4", "W2+S24>12@SS", "W2+S22>12@63", "W2+S24>12@RMD"];

        // What converting heavily early does beyond the 82.5% spend: every household run at the baseline's 82.5% spend
        // on the fit markets, comparing the after-tax money left (today's dollars) in its strong
        // markets (the 90th-percentile path) and its weaker ones (the 25th percentile), against the baseline's own.
        private static List<EarlyConversionTradeoff> EarlyConversionTradeoffs(
            CandidateSet set, List<Scenario> households, Dictionary<int, Dictionary<string, ResultRow>> fit, int paths)
        {
            var baseline = set.Baseline;
            var combinations = HeavyEarlyCodes.Select(code => set.All.Single(c => c.Code == code)).ToList();
            // Per household and combination: the 90th- and 25th-percentile after-tax wealth left; [0] is the baseline
            var wealth = new (double Strong, double Weak)[households.Count][];
            Parallel.For(0, households.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                var s = households[i];
                double spend = fit[s.Id][baseline.Code].Spend825;
                wealth[i] = combinations.Prepend(baseline).Select(c =>
                {
                    var runner = new PathRunner(s.Parameters, c);
                    var left = Enumerable.Range(0, paths).Select(path => runner.Outcome(spend, path).AfterTaxEndingWealth).ToArray();
                    return (Analysis.Percentile(left, 0.9), Analysis.Percentile(left, 0.25));
                }).ToArray();
            });

            return combinations.Select((c, k) =>
            {
                double[] Diffs(Func<(double Strong, double Weak), double> pick) => Enumerable.Range(0, households.Count)
                    .Where(i => pick(wealth[i][0]) > 0)
                    .Select(i => pick(wealth[i][k + 1]) / pick(wealth[i][0]) - 1).ToArray();
                var strong = Diffs(w => w.Strong);
                var weak = Diffs(w => w.Weak);
                return new EarlyConversionTradeoff(
                    c.Code, c.Policy.Name,
                    households.Average(s => fit[s.Id][c.Code].Spend825 / fit[s.Id][baseline.Code].Spend825 - 1),
                    Analysis.Percentile(strong, 0.5), Analysis.Share(strong, v => v > 0),
                    Analysis.Percentile(weak, 0.5), Analysis.Share(weak, v => v > 0));
            }).ToList();
        }

        private static Dictionary<int, Dictionary<string, ResultRow>> Index(IReadOnlyList<ResultRow> rows) =>
            rows.GroupBy(r => r.ScenarioId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Combination));

        private sealed record Rule(string Name, string Description, IReadOnlyList<string> Schedules, Func<Scenario, IReadOnlyList<string>> Candidates);

        // A decision tree: a split on one feature, or a leaf naming the schedule it adds (null: none).
        private sealed record Node(string? Schedule, int Feature = -1, double Threshold = 0, Node? Below = null, Node? Above = null)
        {
            public bool IsLeaf => Below is null;
        }

        // Grows the tree that maximizes the households' summed in-sample gain: each leaf adds the one schedule that
        // gains its households the most (or none), and a split is kept only when it gains more than the leaf it replaces.
        private static Node Grow(List<Scenario> households, List<string> schedules, Func<Scenario, string, double> gain, int depth)
        {
            (string? Code, double Total) BestLeaf(List<Scenario> group)
            {
                if (group.Count == 0) return (null, 0);
                var best = schedules.Select(c => (Code: (string?)c, Total: group.Sum(s => gain(s, c)))).MaxBy(x => x.Total);
                return best.Total > 0 ? best : (null, 0);
            }

            var leaf = BestLeaf(households);
            if (depth == 0) return new Node(leaf.Code);

            (int Feature, double Threshold, double Total)? bestSplit = null;
            for (int f = 0; f < Features.Length; f++)
            {
                var values = households.Select(s => Features[f].Value(s)).OrderBy(v => v).ToArray();
                foreach (double threshold in Enumerable.Range(1, 9).Select(d => Analysis.Percentile(values, d / 10.0)).Distinct())
                {
                    var below = households.Where(s => Features[f].Value(s) < threshold).ToList();
                    var above = households.Where(s => Features[f].Value(s) >= threshold).ToList();
                    if (below.Count < MinLeafSize || above.Count < MinLeafSize) continue;
                    double total = BestLeaf(below).Total + BestLeaf(above).Total;
                    if (bestSplit is null || total > bestSplit.Value.Total) bestSplit = (f, threshold, total);
                }
            }
            if (bestSplit is not { } split || split.Total <= leaf.Total) return new Node(leaf.Code);

            return new Node(null, split.Feature, split.Threshold,
                Grow(households.Where(s => Features[split.Feature].Value(s) < split.Threshold).ToList(), schedules, gain, depth - 1),
                Grow(households.Where(s => Features[split.Feature].Value(s) >= split.Threshold).ToList(), schedules, gain, depth - 1));
        }

        private static string? Leaf(Node node, Scenario s) =>
            node.IsLeaf ? node.Schedule : Leaf(Features[node.Feature].Value(s) < node.Threshold ? node.Below! : node.Above!, s);

        private static IEnumerable<string?> Leaves(Node node) =>
            node.IsLeaf ? [node.Schedule] : Leaves(node.Below!).Concat(Leaves(node.Above!));

        private static string Describe(Node node, string path = "")
        {
            if (node.IsLeaf)
                return $"{(path.Length == 0 ? "Everyone" : path)}: {(node.Schedule is null ? "today's pick" : $"today's pick plus {node.Schedule}")}.";
            var (name, _, format) = Features[node.Feature];
            string Join(string condition) => path.Length == 0 ? condition : $"{path}, {condition}";
            return Describe(node.Below!, Join($"{name} under {format(node.Threshold)}")) + " "
                 + Describe(node.Above!, Join($"{name} {format(node.Threshold)} or more"));
        }
    }

    // One rule, judged on the holdout households (out of sample) against today's pick measured the same way; the
    // in-sample gain (fit households, fit markets) is shown beside it to make any overfitting visible. Gains are
    // fractions of the 82.5% spend (0.012 = +1.2%).
    internal sealed record ScheduleRuleResult(
        string Name, string Description, IReadOnlyList<string> Schedules, double InSampleMeanGain,
        double MeanGain, double[] MeanGainCi, double MedianGain, double WinShare, double LossShare,
        string WorstSlice, double WorstSliceMeanGain, bool Adopted);

    // How a schedule fares in sample: the share of fit households whose best of everything it is, and the average
    // gain from offering it beside the app's eight pairs.
    internal sealed record ScheduleUsage(string Code, string Name, double ShareBest, double MeanAddedGain);

    // TradeOffs (with tradeoffPaths > 0): the "convert heavily early" policies against the baseline beyond the 82.5% spend.
    internal sealed record ScheduleStudyResult(
        int FitHouseholds, int HoldoutHouseholds, int Schedules, double ShareWhereAScheduleIsBest,
        IReadOnlyList<ScheduleRuleResult> Rules, IReadOnlyList<ScheduleUsage> TopSchedules,
        double AdoptMeanGain, double AdoptWorstSliceMean, IReadOnlyList<EarlyConversionTradeoff> TradeOffs);

    // One "convert heavily early" policy against the baseline: its 82.5% spend (mean % difference), and the after-tax
    // money left at the baseline's 82.5% spend in each household's strong markets (90th-percentile path) and weaker ones
    // (25th percentile): the median % difference across households and the share of households where it leaves more.
    // (Survival at that one spend isn't reported: where many markets break even at nearly the same spend - often the
    // years before 59.5 - a spend $100 lower moves it by many points, so its average misleads.)
    internal sealed record EarlyConversionTradeoff(
        string Code, string Name, double MeanSpendDiff,
        double StrongMarketWealthMedianDiff, double StrongMarketWealthHigherShare,
        double WeakMarketWealthMedianDiff, double WeakMarketWealthHigherShare);
}
