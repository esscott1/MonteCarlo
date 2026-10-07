using System.Text.Json;
using System.Text.Json.Nodes;
using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web
{
    // The Scenario runner's results for a visitor without the tax-detail feature: the same response with the tax detail
    // removed from every year after each run's first few (FreeDefaults:TaxDetailTeaserYears, kept as a preview of Plus) -
    // brackets and the room to the next, harvested and 0% gains, Medicare IRMAA and MAGI, Roth conversions and who paid
    // the ordinary tax. Each year's tax totals and effective rate, the spending, which account paid and the balances stay.
    // "taxDetail": false and "taxDetailYears" tell the page.
    public static class FreeRunView
    {
        public static IReadOnlyList<string> TaxDetailFields { get; } = new[]
        {
            nameof(RunYearDetail.OrdinaryBracketRate),
            nameof(RunYearDetail.AmountUntilNextBracket),
            nameof(RunYearDetail.NextBracketRate),
            nameof(RunYearDetail.CapitalGainsBracketRate),
            nameof(RunYearDetail.AmountUntilNextCapitalGainsBracket),
            nameof(RunYearDetail.NextCapitalGainsBracketRate),
            nameof(RunYearDetail.HarvestedGains),
            nameof(RunYearDetail.ZeroRateGains),
            nameof(RunYearDetail.IrmaaSurcharge),
            nameof(RunYearDetail.Magi),
            nameof(RunYearDetail.RothConversionAmount),
            nameof(RunYearDetail.RothConversionTax),
            nameof(RunYearDetail.TaxDeferredWithdrawalTax),
            nameof(RunYearDetail.RothConversionOrdinaryTaxFromBrokerage),
            nameof(RunYearDetail.RothConversionOrdinaryTaxFromConversion),
        }.Select(JsonNamingPolicy.CamelCase.ConvertName).ToArray();

        private static readonly string LifetimeIrmaa = JsonNamingPolicy.CamelCase.ConvertName(nameof(RunSummary.LifetimeIrmaaSurcharges));

        private static readonly string YearField = JsonNamingPolicy.CamelCase.ConvertName(nameof(RunYearDetail.Year));

        public static JsonObject From(object response, JsonSerializerOptions options, int teaserYears)
        {
            var root = JsonSerializer.SerializeToNode(response, options)!.AsObject();
            var output = root["output"]!.AsObject();
            foreach (var run in output["result"]!["runs"]!.AsArray())
            {
                run!.AsObject().Remove(LifetimeIrmaa);
                foreach (var year in run["years"]!.AsArray()) Strip(year!.AsObject(), teaserYears);
            }
            if (output["lastSuccessfulRun"] is JsonArray last)
            {
                foreach (var year in last) Strip(year!.AsObject(), teaserYears);
            }
            root["taxDetail"] = false;
            root["taxDetailYears"] = teaserYears;
            return root;
        }

        // A year from the teaser on (model year 0 is the retirement year) loses its detail
        private static void Strip(JsonObject year, int teaserYears)
        {
            if (year[YearField]!.GetValue<int>() < teaserYears) return;
            foreach (var field in TaxDetailFields) year.Remove(field);
        }
    }
}
