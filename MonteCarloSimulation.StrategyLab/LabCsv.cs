using System.Globalization;

namespace MonteCarloSimulation.StrategyLab
{
    // The lab's raw outputs. Invariant culture, round-trippable numbers, one header row.
    internal static class LabCsv
    {
        private const string ResultsHeader =
            "scenario_id,combination,spend_825,spend_50,common_spend,survival_at_common,median_taxes_at_common,median_after_tax_wealth_at_common";

        public static void WriteScenarios(string path, IReadOnlyList<Scenario> scenarios)
        {
            using var w = new StreamWriter(path);
            w.WriteLine("scenario_id,retirement_date,retirement_age,birthdate,years,total_assets,tax_deferred_share,roth_share,brokerage_gain_fraction," +
                        "tax_deferred,roth_basis,roth_gain,brokerage_basis,brokerage_gain,ss_monthly,ss_start_age,new_money,year_new_money,investment_scenario");
            foreach (var s in scenarios)
            {
                var p = s.Parameters;
                w.WriteLine(Join(s.Id, p.RetirementDate.ToString("yyyy-MM-dd"), s.RetirementAge, p.Birthdate.ToString("yyyy-MM-dd"), p.Years, s.TotalAssets,
                    s.TaxDeferredShare, s.RothShare, s.BrokerageGainFraction, p.InitialTaxableBalance, p.InitialRothBasis, p.InitialRothUnrealizedGain,
                    p.InitialBrokerageBasis, p.InitialBrokerageUnrealizedGain, p.SocialSecurityMonthlyAmount, s.SocialSecurityStartAge,
                    p.NewMoney, p.YearNewMoney, s.InvestmentScenarioId));
            }
        }

        public static void WriteResults(string path, IReadOnlyList<ResultRow> rows)
        {
            using var w = new StreamWriter(path);
            w.WriteLine(ResultsHeader);
            foreach (var r in rows) w.WriteLine(Line(r));
        }

        public static void AppendResults(string path, IReadOnlyList<ResultRow> rows)
        {
            using var w = new StreamWriter(path, append: true);
            foreach (var r in rows) w.WriteLine(Line(r));
        }

        public static List<ResultRow> ReadResults(string path)
        {
            if (!File.Exists(path)) return [];
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0] != ResultsHeader) return [];
            return lines.Skip(1).Where(l => l.Length > 0).Select(l =>
            {
                var f = l.Split(',');
                double D(int i) => double.Parse(f[i], CultureInfo.InvariantCulture);
                return new ResultRow(int.Parse(f[0], CultureInfo.InvariantCulture), f[1], D(2), D(3), D(4), D(5), D(6), D(7));
            }).ToList();
        }

        public static void WriteSingleYear(string path, IReadOnlyList<SingleYearCase> cases)
        {
            using var w = new StreamWriter(path);
            w.WriteLine("id,need,ss_taxable,inflation_factor,age_eligible,eligible_tax_deferred,brokerage,gain_fraction,eligible_roth," +
                        "plan_gross_tax_deferred,plan_gross_brokerage,plan_roth,plan_tax,best_gross_tax_deferred,best_gross_brokerage,best_tax,gap,shortfall,roth_before_others");
            foreach (var c in cases)
                w.WriteLine(Join(c.Id, c.Need, c.SsTaxable, c.InflationFactor, c.AgeEligible, c.EligibleTaxable, c.Brokerage, c.GainFraction, c.EligibleRoth,
                    c.PlanGrossTaxable, c.PlanGrossBrokerage, c.PlanRoth, c.PlanTax, c.BestGrossTaxable, c.BestGrossBrokerage, c.BestTax, c.Gap,
                    c.Shortfall, c.RothBeforeOthers));
        }

        private static string Line(ResultRow r) =>
            Join(r.ScenarioId, r.Combination, r.Spend825, r.Spend50, r.CommonSpend, r.SurvivalAtCommon, r.MedianTaxesAtCommon, r.MedianAfterTaxWealthAtCommon);

        private static string Join(params object[] values) =>
            string.Join(",", values.Select(v => v switch
            {
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => v.ToString()
            }));
    }
}
