using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web
{
    // The Scenario runner's results for a visitor without the tax-detail feature: the same response with the tax detail
    // removed from every year after each run's first few (FreeDefaults:TaxDetailTeaserYears, kept as a preview of Plus) -
    // brackets and the room to the next, harvested and 0% gains, Medicare IRMAA and MAGI, Roth conversions and who paid
    // the ordinary tax. Each year's tax totals and effective rate, the spending, which account paid and the balances stay.
    // "taxDetail": false and "taxDetailYears" tell the page.
    //
    // The detail is left out as the response is written, in one pass: a type-info modifier skips those properties. (It
    // used to be serialized into a JsonNode tree, trimmed and serialized again, which cost several times the response.)
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

        public static IResult Result(SimulationParameters parameters, SimulationRunOutput output, JsonSerializerOptions appOptions, int teaserYears) =>
            Results.Json(new FreeRunResponse(parameters, output, TaxDetail: false, teaserYears), OptionsFor(appOptions, teaserYears));

        // The options a Free response is written with: the app's plus the modifier. Built once per (app options, teaser
        // years): options are costly to build, and the teaser setting can change while the app runs.
        private static readonly ConcurrentDictionary<(JsonSerializerOptions AppOptions, int TeaserYears), JsonSerializerOptions> Options = new();

        public static JsonSerializerOptions OptionsFor(JsonSerializerOptions appOptions, int teaserYears) =>
            Options.GetOrAdd((appOptions, teaserYears), key => new JsonSerializerOptions(key.AppOptions)
            {
                TypeInfoResolver = (key.AppOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
                    .WithAddedModifier(info => LeaveOutDetail(info, key.TeaserYears))
            });

        // A year from the teaser on (model year 0 is the retirement year) loses its detail, and a run its lifetime IRMAA
        private static void LeaveOutDetail(JsonTypeInfo info, int teaserYears)
        {
            if (info.Type == typeof(RunYearDetail))
            {
                foreach (var property in info.Properties)
                    if (TaxDetailFields.Contains(property.Name)) // the JSON (camelCase) name
                        property.ShouldSerialize = (year, _) => ((RunYearDetail)year).Year < teaserYears;
            }
            else if (info.Type == typeof(RunSummary))
            {
                foreach (var property in info.Properties)
                    if (property.Name == LifetimeIrmaa)
                        property.ShouldSerialize = (_, _) => false;
            }
        }
    }

    // What /api/run sends a visitor without the tax-detail feature: the run's parameters and output (written through
    // FreeRunView's options) and the two flags the page reads
    public sealed record FreeRunResponse(SimulationParameters Parameters, SimulationRunOutput Output, bool TaxDetail, int TaxDetailYears);
}
