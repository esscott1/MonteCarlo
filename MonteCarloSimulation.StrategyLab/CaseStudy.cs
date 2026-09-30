namespace MonteCarloSimulation.StrategyLab
{
    // The Model Info page's worked example of why the baseline loses where it loses most: the household where
    // Pro-rata + app conversions beats today's default by the most, and the first market path on which, at the
    // baseline's 82.5% spend, the baseline fails and Pro-rata survives. Both runs are shown year by year up to the
    // baseline's failure year.
    internal sealed record CaseStudy(
        string Scenario,
        string BaselineCombination,
        string AlternativeCombination,
        double BaselineSpend825,
        double AlternativeSpend825,
        double Spend,
        int Path,
        int? BaselineFailureCalendarYear,
        IReadOnlyList<CaseStudyYear> Baseline,
        IReadOnlyList<CaseStudyYear> Alternative)
    {
        public static readonly string AlternativeCode = Candidates.All.Single(c => c.Order == Candidates.ProRata && c.Policy == Candidates.AppDefault).Code;

        public static CaseStudy? Build(IReadOnlyList<Scenario> scenarios, IReadOnlyList<ResultRow> rows, int paths)
        {
            var baseline = Candidates.Baseline;
            var alternative = Candidates.All.Single(c => c.Code == AlternativeCode);
            var byScenario = rows.GroupBy(r => r.ScenarioId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Combination));

            var pick = scenarios
                .Where(s => byScenario.TryGetValue(s.Id, out var r) && r.ContainsKey(baseline.Code) && r.ContainsKey(alternative.Code)
                            && r[baseline.Code].Spend825 >= Analysis.MinimumBaselineSpend)
                .MaxBy(s => byScenario[s.Id][alternative.Code].Spend825 / byScenario[s.Id][baseline.Code].Spend825);
            if (pick is null) return null;

            var results = byScenario[pick.Id];
            double spend = results[baseline.Code].CommonSpend;
            var baselineRunner = new PathRunner(pick.Parameters, baseline);
            var alternativeRunner = new PathRunner(pick.Parameters, alternative);

            for (int path = 0; path < paths; path++)
            {
                var b = baselineRunner.Run(spend, path, out _);
                if (!b.Failed) continue;
                var a = alternativeRunner.Run(spend, path, out _);
                if (a.Failed) continue;

                int shown = b.Years.Count;
                return new CaseStudy(
                    Analysis.Describe(pick), baseline.Code, alternative.Code,
                    results[baseline.Code].Spend825, results[alternative.Code].Spend825, spend, path,
                    b.Years[^1].CalendarYear,
                    b.Years.Select(CaseStudyYear.From).ToList(),
                    a.Years.Take(shown).Select(CaseStudyYear.From).ToList());
            }
            return null;
        }
    }

    // One year of a case-study run: where the spending came from, what was converted, and what was left.
    internal sealed record CaseStudyYear(
        int CalendarYear,
        double Age,
        double TaxDeferredWithdrawal,
        double BrokerageSold,
        double RothSpent,
        double Converted,
        double Taxes,
        double RothBalance,
        double TotalBalance)
    {
        public static CaseStudyYear From(Core.RunYearDetail y) => new(
            y.CalendarYear, y.AgeInYear, y.TaxableWithdrawal, y.BrokerageWithdrawal, y.RothWithdrawal, y.RothConversionAmount,
            y.OrdinaryTaxAmount + y.CapitalGainsTaxAmount, y.RothBalance, y.Balance);
    }
}
