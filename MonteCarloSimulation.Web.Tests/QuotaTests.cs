using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MonteCarloSimulation.Web.Tests
{
    // The passphrase forms' request limits: 5 per address over a rolling hour, refusals that say exactly when the next
    // attempt is allowed, and the allowance reported on every response.
    public class QuotaTests
    {
        private sealed class TestClock(DateTimeOffset start) : TimeProvider
        {
            private DateTimeOffset _now = start;
            public override DateTimeOffset GetUtcNow() => _now;
            public void Advance(TimeSpan by) => _now += by;
        }

        private static readonly DateTimeOffset Start = new(2026, 10, 6, 16, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

        // --- RequestQuota ---

        [Fact]
        public void FiveAllowed_TheSixthRefused_UntilAnHourAfterTheFirst()
        {
            var clock = new TestClock(Start);
            var quota = new RequestQuota(5, Hour, clock);

            for (int i = 0; i < 5; i++)
            {
                var allowed = quota.TryAcquire("a");
                Assert.True(allowed.Allowed);
                Assert.Equal(4 - i, allowed.Remaining);
                clock.Advance(TimeSpan.FromMinutes(10));   // requests at :00, :10, :20, :30, :40
            }

            var refused = quota.TryAcquire("a");          // at :50
            Assert.False(refused.Allowed);
            Assert.Equal(0, refused.Remaining);
            Assert.Equal(Start + Hour, refused.NextSlotAt);
            Assert.Equal(TimeSpan.FromMinutes(10), refused.RetryAfter);
        }

        [Fact]
        public void SlotsFreeUpOneAtATime_AsTheOldestRequestsAgeOut()
        {
            var clock = new TestClock(Start);
            var quota = new RequestQuota(5, Hour, clock);
            for (int i = 0; i < 5; i++)
            {
                quota.TryAcquire("a");
                clock.Advance(TimeSpan.FromMinutes(10));
            }

            clock.Advance(TimeSpan.FromMinutes(10));      // now 17:00: the 16:00 request has aged out
            var first = quota.TryAcquire("a");
            Assert.True(first.Allowed);
            Assert.Equal(0, first.Remaining);
            Assert.Equal(Start + TimeSpan.FromMinutes(10) + Hour, first.NextSlotAt);   // the 16:10 request is next

            var second = quota.TryAcquire("a");           // still 17:00: only one slot freed
            Assert.False(second.Allowed);
            Assert.Equal(Start + TimeSpan.FromMinutes(10) + Hour, second.NextSlotAt);
        }

        [Fact]
        public void RefusedAttempts_DontCount_AndAddressesAreSeparate()
        {
            var clock = new TestClock(Start);
            var quota = new RequestQuota(5, Hour, clock);
            for (int i = 0; i < 5; i++) quota.TryAcquire("a");
            for (int i = 0; i < 10; i++) Assert.False(quota.TryAcquire("a").Allowed);

            Assert.True(quota.TryAcquire("b").Allowed);

            clock.Advance(Hour);
            var again = quota.TryAcquire("a");
            Assert.True(again.Allowed);
            Assert.Equal(4, again.Remaining);             // the refused attempts weren't recorded
        }

        // --- The endpoints ---

        private static (WebApplicationFactory<Program> Factory, TestClock Clock) AppWithClock()
        {
            var clock = new TestClock(Start);
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
            return (factory, clock);
        }

        private static StringContent Json(object body) =>
            new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static string Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? values.Single() : "";

        [Theory]
        [InlineData("/api/change-request", "Too many change requests from this address.")]
        [InlineData("/api/observe-access", "Too many attempts from this address.")]
        public async Task EveryAttemptCounts_AndTheSixthIsRefusedWithTheExactRetryTime(string path, string message)
        {
            var (factory, clock) = AppWithClock();
            using var _ = factory;
            var client = factory.CreateClient();

            // Invalid input still counts: it's refused by validation (400) after the quota is spent
            for (int i = 0; i < 5; i++)
            {
                using var attempt = await client.PostAsync(path, Json(new { }));
                Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
                Assert.Equal("5", Header(attempt, QuotaFilter.LimitHeader));
                Assert.Equal((4 - i).ToString(), Header(attempt, QuotaFilter.RemainingHeader));
                Assert.Equal(Start + Hour, DateTimeOffset.Parse(Header(attempt, QuotaFilter.NextSlotHeader)));
                clock.Advance(TimeSpan.FromMinutes(1));
            }

            using var refused = await client.PostAsync(path, Json(new { }));
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
            Assert.Equal(TimeSpan.FromMinutes(55), refused.Headers.RetryAfter?.Delta);
            using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
            Assert.Equal(message, body.RootElement.GetProperty("message").GetString());
            Assert.Equal(Start + Hour, body.RootElement.GetProperty("retryAt").GetDateTimeOffset());
            Assert.Equal(5, body.RootElement.GetProperty("limit").GetInt32());

            // An hour after the first attempt, one slot is free again
            clock.Advance(TimeSpan.FromMinutes(55));
            using var later = await client.PostAsync(path, Json(new { }));
            Assert.Equal(HttpStatusCode.BadRequest, later.StatusCode);
            Assert.Equal("0", Header(later, QuotaFilter.RemainingHeader));
        }

        [Fact]
        public async Task TheTwoFormsHaveSeparateQuotas()
        {
            var (factory, _) = AppWithClock();
            using var __ = factory;
            var client = factory.CreateClient();
            for (int i = 0; i < 5; i++) (await client.PostAsync("/api/change-request", Json(new { }))).Dispose();

            using var changeRequest = await client.PostAsync("/api/change-request", Json(new { }));
            using var observe = await client.PostAsync("/api/observe-access", Json(new { passphrase = "" }));

            Assert.Equal(HttpStatusCode.TooManyRequests, changeRequest.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, observe.StatusCode);
            Assert.Equal("4", Header(observe, QuotaFilter.RemainingHeader));
        }

        [Theory]
        [InlineData("/index.html", "app.js")]
        [InlineData("/", "menu.js")]
        public async Task Pages_LoadQuotaJs_BeforeTheScriptThatUsesIt(string page, string script)
        {
            using var factory = new WebApplicationFactory<Program>();
            string html = await factory.CreateClient().GetStringAsync(page);

            int quota = html.IndexOf("<script src=\"quota.js\"></script>");
            int user = html.IndexOf($"<script src=\"{script}\"></script>");
            Assert.True(quota >= 0 && user > quota, $"{page} must load quota.js before {script}");
        }
    }
}
