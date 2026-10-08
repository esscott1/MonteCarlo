using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MonteCarloSimulation.Web.Tests
{
    // The Observe page's Visits section: POST /api/visits counts a visit toward today (a Mountain-time day), GET reports
    // the last 90 days behind the Observe token, and the counts are kept in a file that survives a restart.
    public class VisitsTests
    {
        private const string Passphrase = "observe-test-passphrase";

        private sealed class TestClock(DateTimeOffset start) : TimeProvider
        {
            private DateTimeOffset _now = start;
            public override DateTimeOffset GetUtcNow() => _now;
            public void Set(DateTimeOffset now) => _now = now;
        }

        // Noon in Denver (MDT, UTC-6) on Thursday, October 8, 2026
        private static readonly DateTimeOffset Noon = new(2026, 10, 8, 18, 0, 0, TimeSpan.Zero);

        private static string NewPath() => Path.Combine(Path.GetTempPath(), "montecarlo-tests", $"visits-{Guid.NewGuid():N}.json");

        private static WebApplicationFactory<Program> App(string path, TestClock clock) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Visits:Path", path);
                builder.UseSetting("SiteFlags:Path", TempSiteFlags.NewPath());
                builder.UseSetting("ChangeRequest:Passphrase", Passphrase);
                builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
            });

        private static async Task VisitAsync(HttpClient client, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                using var response = await client.PostAsync("/api/visits", null);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }
        }

        private static async Task<JsonElement> ReportAsync(HttpClient client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/visits");
            request.Headers.Add(ObserveAccessToken.Header, ObserveAccessToken.Issue(Passphrase));
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        private static int VisitsOn(JsonElement report, string date) =>
            report.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("date").GetString() == date).GetProperty("visits").GetInt32();

        [Theory]
        [InlineData(null)]
        [InlineData("not-a-token")]
        public async Task WithoutAValidObserveToken_TheReportIsA401(string? token)
        {
            using var app = App(NewPath(), new TestClock(Noon));
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/visits");
            if (token is not null) request.Headers.Add(ObserveAccessToken.Header, token);

            using var response = await app.CreateClient().SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task EachVisit_CountsTowardToday()
        {
            using var app = App(NewPath(), new TestClock(Noon));
            var client = app.CreateClient();

            await VisitAsync(client, 3);
            var report = await ReportAsync(client);

            Assert.Equal("2026-10-08", report.GetProperty("today").GetString());
            Assert.Equal("America/Denver", report.GetProperty("timeZone").GetString());
            Assert.Equal("2026-10-08", report.GetProperty("countingSince").GetString());
            Assert.Equal(3, VisitsOn(report, "2026-10-08"));
            Assert.Equal(3, report.GetProperty("last7").GetInt32());
            // Counting started today, so the average is over one day, not thirty
            Assert.Equal(3.0, report.GetProperty("averageDaily30").GetDouble());
        }

        [Fact]
        public async Task TheReport_IsTheLast90Days_WithEachRangesTotal_AndTheAverageOverThe30()
        {
            string path = NewPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {
                  "2026-06-01": 50,
                  "2026-07-11": 9,
                  "2026-09-09": 5,
                  "2026-10-02": 7,
                  "2026-10-08": 1
                }
                """);
            using var app = App(path, new TestClock(Noon));

            var report = await ReportAsync(app.CreateClient());

            var days = report.GetProperty("days").EnumerateArray().ToList();
            Assert.Equal(VisitCounter.ReportDays, days.Count);
            Assert.Equal("2026-07-11", days[0].GetProperty("date").GetString());   // today and the 89 days before it
            Assert.Equal("2026-10-08", days[^1].GetProperty("date").GetString());
            Assert.Equal(0, VisitsOn(report, "2026-10-01"));                       // days without visits are filled in
            Assert.Equal(8, report.GetProperty("last7").GetInt32());                // Oct 2 - Oct 8
            Assert.Equal(13, report.GetProperty("last30").GetInt32());              // Sep 9 - Oct 8
            Assert.Equal(22, report.GetProperty("last90").GetInt32());              // Jul 11 - Oct 8; June is too old
            Assert.Equal("2026-06-01", report.GetProperty("countingSince").GetString());
            Assert.Equal(0.4, report.GetProperty("averageDaily30").GetDouble());   // 13 / 30
        }

        [Fact]
        public async Task ADay_IsAMountainTimeDay()
        {
            var clock = new TestClock(new DateTimeOffset(2026, 10, 9, 5, 59, 0, TimeSpan.Zero));   // 11:59 pm Oct 8 in Denver
            using var app = App(NewPath(), clock);
            var client = app.CreateClient();

            await VisitAsync(client);
            clock.Set(new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.Zero));                     // midnight Oct 9 in Denver
            await VisitAsync(client, 2);
            var report = await ReportAsync(client);

            Assert.Equal(1, VisitsOn(report, "2026-10-08"));
            Assert.Equal(2, VisitsOn(report, "2026-10-09"));
        }

        [Fact]
        public async Task TheCounts_AreKept_AcrossARestart()
        {
            string path = NewPath();
            using (var first = App(path, new TestClock(Noon)))
                await VisitAsync(first.CreateClient(), 2);

            using var restarted = App(path, new TestClock(Noon));
            var client = restarted.CreateClient();
            await VisitAsync(client);

            Assert.Equal(3, VisitsOn(await ReportAsync(client), "2026-10-08"));
        }

        [Fact]
        public async Task OneAddress_CanCount30VisitsAnHour_NotMore()
        {
            using var app = App(NewPath(), new TestClock(Noon));
            var client = app.CreateClient();

            await VisitAsync(client, 30);
            using var refused = await client.PostAsync("/api/visits", null);

            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
            Assert.Equal(30, VisitsOn(await ReportAsync(client), "2026-10-08"));
        }
    }
}
