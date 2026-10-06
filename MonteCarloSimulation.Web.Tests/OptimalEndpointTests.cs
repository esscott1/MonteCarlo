using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.Web.Tests
{
    // POST /api/optimal's contract: invalid input is a 400 with field errors, as before; valid input streams
    // newline-delimited JSON - start, a progress event per claiming age, each scenario once, then done - and the
    // streamed scenarios are exactly what SpendingOptimizer.Optimize computes for the same inputs.
    public class OptimalEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
    {
        // The Optimal page's default inputs
        private static OptimalRequest DefaultRequest() => new()
        {
            Years = 30,
            Birthdate = new DateOnly(1970, 1, 1),
            RetirementDate = new DateOnly(2027, 1, 1),
            InitialTaxableBalance = 950_000,
            InitialRothBasis = 15_000,
            InitialRothUnrealizedGain = 5_000,
            InitialBrokerageBasis = 200_000,
            InitialBrokerageUnrealizedGain = 200_000,
            NewMoney = 1_000_000,
            YearNewMoney = 10,
            SocialSecurityAt62 = 2_750,
            SocialSecurityAt67 = 3_900,
            SocialSecurityAt70 = 4_800,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true
        };

        private JsonSerializerOptions AppJsonOptions => factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        private async Task<HttpResponseMessage> PostAsync(OptimalRequest request) =>
            await factory.CreateClient().PostAsync("/api/optimal",
                new StringContent(JsonSerializer.Serialize(request, AppJsonOptions), Encoding.UTF8, "application/json"));

        [Fact]
        public async Task InvalidRequest_Returns400WithFieldErrors_NotAStream()
        {
            var request = DefaultRequest();
            request.Years = 0;
            request.SocialSecurityAt62 = 5_000; // more than at 67

            using var response = await PostAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var errors = body.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty("years", out _));
            Assert.True(errors.TryGetProperty("socialSecurity", out _));
        }

        // Null: the request leaves Paths out, which runs the default 500
        [Theory]
        [InlineData(null)]
        [InlineData(167)]
        public async Task ValidRequest_StreamsStartProgressEachScenarioAndDone_MatchingTheOptimizer(int? paths)
        {
            var request = DefaultRequest();
            request.Paths = paths;

            using var response = await PostAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(OptimalStream.ContentType, response.Content.Headers.ContentType?.MediaType);
            var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var events = lines.Select(line => JsonDocument.Parse(line).RootElement).ToList();
            string TypeOf(JsonElement e) => e.GetProperty("type").GetString()!;

            // start first, done last, nothing else but progress and scenarios between
            Assert.Equal("start", TypeOf(events[0]));
            Assert.Equal("done", TypeOf(events[^1]));
            Assert.All(events.Skip(1).SkipLast(1), e => Assert.Contains(TypeOf(e), new[] { "progress", "scenario" }));

            var inputs = request.ToInputs();
            Assert.Equal(paths ?? SpendingOptimizer.DefaultPaths, inputs.Paths);
            int total = inputs.Scenarios.Count * SpendingOptimizer.ClaimingAges.Count;
            var start = events[0];
            Assert.Equal(inputs.Paths, start.GetProperty("paths").GetInt32());
            Assert.Equal(total, start.GetProperty("totalClaimingAges").GetInt32());
            Assert.Equal(inputs.Scenarios.Select(s => s.Id), start.GetProperty("scenarios").EnumerateArray().Select(s => s.GetProperty("scenarioId").GetInt32()));

            // One progress event per claiming age, each count once (worker threads may report slightly out of order)
            var progress = events.Where(e => TypeOf(e) == "progress").ToList();
            Assert.Equal(Enumerable.Range(1, total), progress.Select(p => p.GetProperty("completed").GetInt32()).Order());
            Assert.All(progress, p => Assert.Equal(total, p.GetProperty("total").GetInt32()));

            // Each scenario once, serialized exactly as the optimizer's own result would be
            var expected = SpendingOptimizer.Optimize(inputs);
            var streamed = events.Where(e => TypeOf(e) == "scenario").Select(e => e.GetProperty("scenario")).ToList();
            Assert.Equal(expected.Scenarios.Count, streamed.Count);
            foreach (var scenario in expected.Scenarios)
            {
                var match = Assert.Single(streamed, s => s.GetProperty("scenarioId").GetInt32() == scenario.ScenarioId);
                Assert.Equal(JsonSerializer.Serialize(scenario, AppJsonOptions), match.GetRawText());
            }
            Assert.Equal(JsonSerializer.Serialize(expected.BenefitByAge, AppJsonOptions), start.GetProperty("benefitByAge").GetRawText());

            // Enums travel as names, which optimal.js looks up
            Assert.Equal(JsonValueKind.String, streamed[0].GetProperty("recommended").GetProperty("withdrawalStrategy").ValueKind);
        }

        // --- Choosing how many market paths to simulate ---

        [Fact]
        public void PathChoices_AreAllTwoThirdsAndOneThirdOfTheDefault()
        {
            Assert.Equal(new[] { 500, 333, 167 }, SpendingOptimizer.PathChoices);
            Assert.Equal(SpendingOptimizer.DefaultPaths, SpendingOptimizer.PathChoices[0]);
        }

        [Theory]
        [InlineData(null, 500)]
        [InlineData(500, 500)]
        [InlineData(333, 333)]
        [InlineData(167, 167)]
        public void Paths_EachChoiceIsAccepted_AndOmittedMeansTheDefault(int? paths, int expected)
        {
            var request = DefaultRequest();
            request.Paths = paths;

            Assert.Empty(request.Validate());
            Assert.Equal(expected, request.ToInputs().Paths);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(250)]
        [InlineData(1_000)]
        [InlineData(-167)]
        public void Paths_AnythingElseIsAFieldError(int paths)
        {
            var request = DefaultRequest();
            request.Paths = paths;

            Assert.True(request.Validate().ContainsKey("paths"));
        }

        [Fact]
        public async Task InvalidPaths_Returns400WithAPathsError()
        {
            var request = DefaultRequest();
            request.Paths = 250;

            using var response = await PostAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("paths", out _));
        }

        [Fact]
        public async Task OptimalPage_OffersExactlyThePathChoices_WithTheDefaultChecked()
        {
            string html = await factory.CreateClient().GetStringAsync("/optimal.html");

            var radios = Regex.Matches(html, @"<input type=""radio"" name=""paths"" value=""(\d+)""( checked)?>");
            Assert.Equal(SpendingOptimizer.PathChoices, radios.Select(m => int.Parse(m.Groups[1].Value)));
            var checkedValue = Assert.Single(radios, m => m.Groups[2].Success);
            Assert.Equal(SpendingOptimizer.DefaultPaths, int.Parse(checkedValue.Groups[1].Value));
        }
    }
}
