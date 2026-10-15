using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MonteCarloSimulation.Web.Tests
{
    // The Annuity tab's suggested SPIA rate: the LifeAnnuities.us SPIA Payout Index (never fetched for real here: a stub
    // handler stands in), estimated for any age 55-80 by SpiaPayoutModel, fetched at most once a week by the first request
    // after Monday 6:00 AM Mountain, a failed fetch leaving the stored figures quietly in place. Plus gets the male rate;
    // choosing the annuitant's sex is Pro's.
    public class SpiaRateTests
    {
        private const string PlusCode = "plus-test-code";
        private const string ProCode = "pro-test-code";

        private static readonly SpiaCells October = new(Male65: 693, Male70: 767, Male75: 893, Female65: 658, Joint65: 608);

        // --- The model ---

        // Two parameters for the men's three figures, so within about 1% (at those ages the endpoint gives the figure itself)
        [Fact]
        public void TheModel_FitsTheIndexsOwnFigures()
        {
            var model = SpiaPayoutModel.Fit(October);

            Assert.InRange(model.Estimate(65, AnnuitantSex.Male) / 693, 0.99, 1.01);
            Assert.InRange(model.Estimate(70, AnnuitantSex.Male) / 767, 0.99, 1.01);
            Assert.InRange(model.Estimate(75, AnnuitantSex.Male) / 893, 0.99, 1.01);
            Assert.InRange(model.Estimate(65, AnnuitantSex.Female) / 658, 0.999, 1.001);
        }

        // Source: LifeAnnuities.us SPIA quote surveys, October 1, 2026, https://lifeannuities.us/rates/best-spia-rates/ -
        // the best monthly income per $100,000 by age (single life, 5-year period certain). Only the 65/70/75 men's and
        // 65 women's figures are in the index the model is fitted to; the rest check its estimates.
        [Theory]
        [InlineData(55, 616, 602)]
        [InlineData(60, 649, 634)]
        [InlineData(65, 693, 658)]
        [InlineData(70, 767, 724)]
        [InlineData(75, 893, 839)]
        [InlineData(80, 1046, 986)]
        public void TheModel_IsWithin4PercentOfThePublishedTable(int age, double male, double female)
        {
            var model = SpiaPayoutModel.Fit(October);

            Assert.InRange(model.Estimate(age, AnnuitantSex.Male) / male, 0.96, 1.04);
            Assert.InRange(model.Estimate(age, AnnuitantSex.Female) / female, 0.96, 1.04);
        }

        [Fact]
        public void Rates_RiseWithAge_AreLowerForWomen_AndStopOutside55To80()
        {
            var model = SpiaPayoutModel.Fit(October);

            for (int age = 55; age < 80; age++)
                Assert.True(model.MonthlyPer100k(age + 1, AnnuitantSex.Male)!.Value.Monthly > model.MonthlyPer100k(age, AnnuitantSex.Male)!.Value.Monthly);
            Assert.True(model.MonthlyPer100k(61, AnnuitantSex.Female)!.Value.Monthly < model.MonthlyPer100k(61, AnnuitantSex.Male)!.Value.Monthly);
            Assert.Null(model.MonthlyPer100k(54.4, AnnuitantSex.Male));
            Assert.Null(model.MonthlyPer100k(80.6, AnnuitantSex.Male));
            // The index's own figure at its ages (nearest birthday), estimated elsewhere
            Assert.Equal((693.0, false), model.MonthlyPer100k(64.6, AnnuitantSex.Male));
            Assert.True(model.MonthlyPer100k(61, AnnuitantSex.Male)!.Value.Estimated);
        }

        // --- The endpoint and the weekly fetch ---

        // Monday, October 12, 2026 6:00 AM in Denver (MDT, UTC-6)
        private static readonly DateTimeOffset MondaySix = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        private sealed class TestClock(DateTimeOffset start) : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = start;
            public override DateTimeOffset GetUtcNow() => Now;
        }

        // Stands in for lifeannuities.us: answers with Respond, counting calls
        private sealed class StubIndex : HttpMessageHandler
        {
            public int Calls;
            public Func<Task<HttpResponseMessage>> Respond = () => Task.FromResult(Ok(Index(October, "2026-10-01")));

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                return await Respond();
            }

            public static HttpResponseMessage Ok(string json) =>
                new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static string Index(SpiaCells cells, string asOf) => JsonSerializer.Serialize(new
        {
            url = "https://lifeannuities.us/rates/spia-payout-index/",
            license = "https://creativecommons.org/licenses/by/4.0/",
            attribution = "Source: LifeAnnuities.us SPIA Payout Index (CC BY 4.0)",
            basis = "Monthly income per $100,000 single premium; 5-year period certain.",
            current = new
            {
                asOf,
                male_65 = cells.Male65, male_70 = cells.Male70, male_75 = cells.Male75,
                female_65 = cells.Female65, joint_65 = cells.Joint65
            }
        });

        private static string NewStorePath() =>
            Path.Combine(Path.GetTempPath(), "montecarlo-tests", $"spia-{Guid.NewGuid():N}.json");

        private static WebApplicationFactory<Program> App(string storePath, TestClock clock, StubIndex index) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("SiteFlags:Path", TempSiteFlags.NewPath());
                builder.UseSetting("SpiaRates:Path", storePath);
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureManagement:TierPlus"] = "true",
                    ["FeatureManagement:TierPro"] = "true",
                    ["Tiers:Plus:AccessCode"] = PlusCode,
                    ["Tiers:Pro:AccessCode"] = ProCode,
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<TimeProvider>(clock);
                    services.AddHttpClient(SpiaPayoutIndex.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(() => index)
                        .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
                });
            });

        private static HttpClient Client(WebApplicationFactory<Program> app) =>
            app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        private static async Task<string> UnlockAsync(HttpClient client, string tier, string code)
        {
            using var response = await client.PostAsync("/api/tier-access",
                new StringContent(JsonSerializer.Serialize(new { tier, code }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        }

        private static async Task<HttpResponseMessage> RateAsync(HttpClient client, string? cookie, string query)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/spia-rate?" + query);
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            return await client.SendAsync(request);
        }

        private static async Task<JsonElement> RateJsonAsync(HttpClient client, string cookie, string query = "age=61&sex=male")
        {
            using var response = await RateAsync(client, cookie, query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        [Fact]
        public async Task Free_IsRefused_PlusGetsTheMaleRate_AndOnlyProMayChooseFemale()
        {
            var index = new StubIndex();
            using var app = App(NewStorePath(), new TestClock(MondaySix.AddDays(2)), index);
            var client = Client(app);

            using (var free = await RateAsync(client, null, "age=61&sex=male"))
                Assert.Equal(HttpStatusCode.Forbidden, free.StatusCode);

            string plus = await UnlockAsync(client, "plus", PlusCode);
            var male = await RateJsonAsync(client, plus);
            Assert.Equal("male", male.GetProperty("sex").GetString());
            Assert.Equal(61, male.GetProperty("age").GetInt32());
            Assert.True(male.GetProperty("estimated").GetBoolean());
            Assert.InRange(male.GetProperty("monthlyPer100k").GetDouble(), 600, 680);
            Assert.Equal("2026-10-01", male.GetProperty("asOf").GetString());
            using (var plusFemale = await RateAsync(client, plus, "age=61&sex=female"))
                Assert.Equal(HttpStatusCode.Forbidden, plusFemale.StatusCode);

            string pro = await UnlockAsync(client, "pro", ProCode);
            var female = await RateJsonAsync(client, pro, "age=61&sex=female");
            Assert.True(female.GetProperty("monthlyPer100k").GetDouble() < male.GetProperty("monthlyPer100k").GetDouble());

            // The index's own figure at 65, and nothing outside 55 to 80 (still with the as-of date)
            Assert.Equal(693, (await RateJsonAsync(client, plus, "age=65.2&sex=male")).GetProperty("monthlyPer100k").GetDouble());
            var tooYoung = await RateJsonAsync(client, plus, "age=50&sex=male");
            Assert.Equal(JsonValueKind.Null, tooYoung.GetProperty("monthlyPer100k").ValueKind);
            Assert.Equal("age", tooYoung.GetProperty("reason").GetString());
            Assert.Equal("2026-10-01", tooYoung.GetProperty("asOf").GetString());
        }

        [Theory]
        [InlineData("sex=male")]
        [InlineData("age=-1&sex=male")]
        [InlineData("age=61&sex=other")]
        public async Task BadInput_IsA400(string query)
        {
            using var app = App(NewStorePath(), new TestClock(MondaySix), new StubIndex());
            var client = Client(app);
            string plus = await UnlockAsync(client, "plus", PlusCode);

            using var response = await RateAsync(client, plus, query);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task TheIndex_IsFetchedOnceAWeek_ByTheFirstRequestAfterMondaySixMountain()
        {
            string store = NewStorePath();
            var clock = new TestClock(MondaySix.AddDays(2)); // Wednesday
            var index = new StubIndex();
            using (var app = App(store, clock, index))
            {
                var client = Client(app);
                string plus = await UnlockAsync(client, "plus", PlusCode);

                // Nothing stored yet: the first request fetches, the rest of the week doesn't
                await RateJsonAsync(client, plus);
                await RateJsonAsync(client, plus);
                clock.Now = MondaySix.AddDays(6).AddHours(17); // Sunday 11:00 PM Mountain
                plus = await UnlockAsync(client, "plus", PlusCode); // the access cookie lasts 12 hours
                await RateJsonAsync(client, plus);
                Assert.Equal(1, index.Calls);

                // Monday 6:30 AM: new figures, which every later response carries
                clock.Now = MondaySix.AddDays(7).AddMinutes(30);
                plus = await UnlockAsync(client, "plus", PlusCode); // the access cookie lasts 12 hours
                index.Respond = () => Task.FromResult(StubIndex.Ok(Index(October with { Male65 = 700 }, "2026-11-01")));
                var monday = await RateJsonAsync(client, plus, "age=65&sex=male");
                Assert.Equal(2, index.Calls);
                Assert.Equal("2026-11-01", monday.GetProperty("asOf").GetString());
                Assert.Equal(700, monday.GetProperty("monthlyPer100k").GetDouble());
            }

            // A restart reads the stored figures and doesn't fetch again that week
            using (var restarted = App(store, clock, index))
            {
                var client = Client(restarted);
                string plus = await UnlockAsync(client, "plus", PlusCode);
                var after = await RateJsonAsync(client, plus, "age=65&sex=male");
                Assert.Equal(2, index.Calls);
                Assert.Equal("2026-11-01", after.GetProperty("asOf").GetString());
            }
        }

        public static TheoryData<string> Failures() => new() { "exception", "timeout", "500", "bad json", "missing figures" };

        private static Func<Task<HttpResponseMessage>> Failing(string how) => how switch
        {
            "exception" => () => throw new HttpRequestException("no route to host"),
            "timeout" => () => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
            "500" => () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            "bad json" => () => Task.FromResult(StubIndex.Ok("{ not json")),
            _ => () => Task.FromResult(StubIndex.Ok("""{ "current": { "asOf": "2026-11-01", "male_65": 700 } }""")),
        };

        [Theory]
        [MemberData(nameof(Failures))]
        public async Task AFailedFetch_QuietlyKeepsTheStoredFigures_AndRetriesAfterAnHour(string how)
        {
            var clock = new TestClock(MondaySix.AddDays(2));
            var index = new StubIndex();
            using var app = App(NewStorePath(), clock, index);
            var client = Client(app);
            string plus = await UnlockAsync(client, "plus", PlusCode);
            var before = await RateJsonAsync(client, plus);

            // Next Monday's fetch fails: the same figures and as-of date, and nothing else in the response
            clock.Now = MondaySix.AddDays(7).AddMinutes(30);
            plus = await UnlockAsync(client, "plus", PlusCode); // the access cookie lasts 12 hours
            index.Respond = Failing(how);
            var failed = await RateJsonAsync(client, plus);
            Assert.Equal(2, index.Calls);
            Assert.Equal(before.ToString(), failed.ToString());
            Assert.Equal(
                new[] { "monthlyPer100k", "age", "sex", "estimated", "reason", "asOf", "basis", "attribution", "sourceUrl", "licenseUrl" },
                failed.EnumerateObject().Select(p => p.Name));

            // Not retried on every request, but an hour later it is
            clock.Now = clock.Now.AddMinutes(59);
            plus = await UnlockAsync(client, "plus", PlusCode); // the access cookie lasts 12 hours
            await RateJsonAsync(client, plus);
            Assert.Equal(2, index.Calls);
            clock.Now = clock.Now.AddMinutes(2);
            plus = await UnlockAsync(client, "plus", PlusCode); // the access cookie lasts 12 hours
            index.Respond = () => Task.FromResult(StubIndex.Ok(Index(October, "2026-11-01")));
            Assert.Equal("2026-11-01", (await RateJsonAsync(client, plus)).GetProperty("asOf").GetString());
            Assert.Equal(3, index.Calls);
        }

        [Fact]
        public async Task WithNothingStored_AndTheFetchFailing_TheSeedFiguresAreUsed()
        {
            var index = new StubIndex { Respond = Failing("exception") };
            using var app = App(NewStorePath(), new TestClock(MondaySix), index);
            var client = Client(app);
            string plus = await UnlockAsync(client, "plus", PlusCode);

            var rate = await RateJsonAsync(client, plus, "age=65&sex=male");

            Assert.Equal(1, index.Calls);
            Assert.False(string.IsNullOrWhiteSpace(rate.GetProperty("asOf").GetString()));
            Assert.True(rate.GetProperty("monthlyPer100k").GetDouble() > 0);
        }

        [Fact]
        public async Task ConcurrentRequests_AtTheRefresh_FetchOnce_AndDontWaitForIt()
        {
            var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var index = new StubIndex { Respond = () => release.Task };
            using var app = App(NewStorePath(), new TestClock(MondaySix.AddHours(1)), index);
            var client = Client(app);
            string plus = await UnlockAsync(client, "plus", PlusCode);

            var fetching = RateJsonAsync(client, plus);
            while (Volatile.Read(ref index.Calls) == 0) await Task.Delay(10);
            // While that request fetches, others answer at once from the stored (seed) figures
            for (int i = 0; i < 5; i++) await RateJsonAsync(client, plus);
            release.SetResult(StubIndex.Ok(Index(October, "2026-10-01")));
            await fetching;

            Assert.Equal(1, index.Calls);
        }
    }
}
