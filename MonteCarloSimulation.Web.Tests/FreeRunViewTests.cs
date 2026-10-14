using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web.Tests
{
    // A Free visitor's results are written in one pass (FreeRunView's options skip the tax detail as they go). They must
    // be byte for byte what the old way produced - serialize to a JsonNode tree, remove the detail, add the flags -
    // which is kept here as the reference.
    public class FreeRunViewTests(PlusAppFactory factory) : IClassFixture<PlusAppFactory>
    {
        private JsonSerializerOptions AppOptions => factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        private sealed record Response(SimulationParameters Parameters, SimulationRunOutput Output);

        // The previous FreeRunView.From, verbatim apart from returning the bytes Results.Json wrote
        private static byte[] Reference(SimulationParameters parameters, SimulationRunOutput output, JsonSerializerOptions options, int teaserYears)
        {
            string lifetimeIrmaa = JsonNamingPolicy.CamelCase.ConvertName(nameof(RunSummary.LifetimeIrmaaSurcharges));
            string yearField = JsonNamingPolicy.CamelCase.ConvertName(nameof(RunYearDetail.Year));
            void Strip(JsonObject year)
            {
                if (year[yearField]!.GetValue<int>() < teaserYears) return;
                foreach (var field in FreeRunView.TaxDetailFields) year.Remove(field);
            }

            var root = JsonSerializer.SerializeToNode(new Response(parameters, output), options)!.AsObject();
            var result = root["output"]!.AsObject();
            foreach (var run in result["result"]!["runs"]!.AsArray())
            {
                run!.AsObject().Remove(lifetimeIrmaa);
                foreach (var year in run["years"]!.AsArray()) Strip(year!.AsObject());
            }
            if (result["lastSuccessfulRun"] is JsonArray last)
            {
                foreach (var year in last) Strip(year!.AsObject());
            }
            root["taxDetail"] = false;
            root["taxDetailYears"] = teaserYears;
            return JsonSerializer.SerializeToUtf8Bytes(root, options);
        }

        private byte[] Direct(SimulationParameters parameters, SimulationRunOutput output, int teaserYears) =>
            JsonSerializer.SerializeToUtf8Bytes(
                new FreeRunResponse(parameters, output, TaxDetail: false, teaserYears), FreeRunView.OptionsFor(AppOptions, teaserYears));

        private static SimulationParameters Parameters(double withdrawal, bool conversions, FilingStatus filing) => new()
        {
            Years = 30,
            Iterations = 20,
            Withdrawal = withdrawal,
            Birthdate = new DateOnly(1962, 4, 20),
            RetirementDate = new DateOnly(2027, 1, 1),
            FilingStatus = filing,
            InitialTaxableBalance = 1_400_000,
            InitialRothBasis = 50_000,
            InitialRothUnrealizedGain = 30_000,
            InitialBrokerageBasis = 200_000,
            InitialBrokerageUnrealizedGain = 250_000,
            AssetMix = new AssetMix
            {
                StockWeight = 0.6, BondWeight = 0.3, CashWeight = 0.1, StockMean = 0.08, StockStdDev = 0.19,
                BondMean = 0.045, BondStdDev = 0.04, CashMean = 0.035, CashStdDev = 0.01, StockBondCorrelation = 0.1
            },
            SocialSecurityStartDate = new DateOnly(2029, 4, 20),
            SocialSecurityMonthlyAmount = 3_000,
            AnnualStandardDeduction = filing == FilingStatus.MarriedJoint ? 32_000 : 16_000,
            EnableRothConversions = conversions,
            ScenarioDescription = "Free view"
        };

        // Low spending (every run survives, so there's a last successful run), middling (some fail), and so high that
        // every run fails (no last successful run); conversions on and off; single and married; teasers from none of the
        // years to more than all of them
        [Theory]
        [InlineData(40_000, true, FilingStatus.Single)]
        [InlineData(110_000, true, FilingStatus.Single)]
        [InlineData(110_000, false, FilingStatus.Single)]
        [InlineData(110_000, true, FilingStatus.MarriedJoint)]
        [InlineData(900_000, true, FilingStatus.Single)]
        public void TheFreeResponse_IsByteForByteWhatTheOldWayWrote(double withdrawal, bool conversions, FilingStatus filing)
        {
            var parameters = Parameters(withdrawal, conversions, filing);
            var output = MonteCarloEngine.Run(parameters);

            foreach (int teaserYears in new[] { 0, 1, 3, 100 })
                Assert.Equal(Reference(parameters, output, AppOptions, teaserYears), Direct(parameters, output, teaserYears));
        }

        [Fact]
        public void EachTeaserSetting_GetsItsOwnOptions()
        {
            var parameters = Parameters(80_000, true, FilingStatus.Single);
            var output = MonteCarloEngine.Run(parameters);
            int YearsWithDetail(int teaserYears)
            {
                using var json = JsonDocument.Parse(Direct(parameters, output, teaserYears));
                return json.RootElement.GetProperty("output").GetProperty("result").GetProperty("runs")[0].GetProperty("years")
                    .EnumerateArray().Count(year => year.TryGetProperty("magi", out _));
            }

            Assert.NotSame(FreeRunView.OptionsFor(AppOptions, 1), FreeRunView.OptionsFor(AppOptions, 3));
            Assert.Same(FreeRunView.OptionsFor(AppOptions, 3), FreeRunView.OptionsFor(AppOptions, 3));
            Assert.Equal(1, YearsWithDetail(1));
            Assert.Equal(3, YearsWithDetail(3));
        }

        [Fact]
        public void TheAppsOwnOptions_StillWriteEveryField()
        {
            var parameters = Parameters(80_000, true, FilingStatus.Single);
            var output = MonteCarloEngine.Run(parameters);
            _ = Direct(parameters, output, 3);

            using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new Response(parameters, output), AppOptions));
            var run = json.RootElement.GetProperty("output").GetProperty("result").GetProperty("runs")[0];
            Assert.True(run.TryGetProperty("lifetimeIrmaaSurcharges", out _));
            Assert.All(run.GetProperty("years").EnumerateArray(),
                year => Assert.All(FreeRunView.TaxDetailFields, field => Assert.True(year.TryGetProperty(field, out _), field)));
        }
    }
}
