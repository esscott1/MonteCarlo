using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MonteCarloSimulation.Web.Tests
{
    // The Observe page's Features section: GET /api/features and PUT /api/features/{plus|pro}, behind the Observe token.
    // A flip applies to every visitor's next request, is kept in the site-flags file (so it survives a restart), and
    // overrides the switch's default from configuration.
    public class SiteFlagsTests
    {
        private const string Passphrase = "observe-test-passphrase";
        private const string PlusCode = "plus-test-code";

        private static WebApplicationFactory<Program> App(string flagsPath, bool? plusDefault = null) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("SiteFlags:Path", flagsPath);
                builder.UseSetting("ChangeRequest:Passphrase", Passphrase);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var settings = new Dictionary<string, string?> { ["Tiers:Plus:AccessCode"] = PlusCode };
                    if (plusDefault is bool plus) settings["FeatureManagement:TierPlus"] = plus.ToString();
                    config.AddInMemoryCollection(settings);
                });
            });

        private static HttpClient Client(WebApplicationFactory<Program> app) =>
            app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        private static HttpRequestMessage Request(HttpMethod method, string path, string? token, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            if (token is not null) request.Headers.Add(ObserveAccessToken.Header, token);
            if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return request;
        }

        private static string Token() => ObserveAccessToken.Issue(Passphrase);

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        private static Task<HttpResponseMessage> FlipAsync(HttpClient client, string tier, bool on, string? token) =>
            client.SendAsync(Request(HttpMethod.Put, $"/api/features/{tier}", token, new { on }));

        private static async Task<string> ModeAsync(HttpClient client) =>
            (await JsonAsync(await client.GetAsync("/api/me"))).GetProperty("mode").GetString()!;

        private static JsonElement TierOf(JsonElement view, string tier) =>
            view.GetProperty("tiers").EnumerateArray().Single(t => t.GetProperty("tier").GetString() == tier);

        [Theory]
        [InlineData(null)]
        [InlineData("not-a-token")]
        public async Task WithoutAValidObserveToken_EveryCallIsA401(string? token)
        {
            using var app = App(TempSiteFlags.NewPath());
            var client = Client(app);

            using var get = await client.SendAsync(Request(HttpMethod.Get, "/api/features", token));
            using var put = await FlipAsync(client, "pro", true, token);

            Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
            Assert.Equal(SiteModes.PlusAvailable, await ModeAsync(client));
        }

        [Fact]
        public async Task TheSwitches_StartAtTheirDefaults_PlusOnAndProOff()
        {
            using var app = App(TempSiteFlags.NewPath());

            var view = await JsonAsync(await Client(app).SendAsync(Request(HttpMethod.Get, "/api/features", Token())));

            Assert.Equal(SiteModes.PlusAvailable, view.GetProperty("mode").GetString());
            var plus = TierOf(view, "Plus");
            Assert.True(plus.GetProperty("on").GetBoolean());
            Assert.Equal(JsonValueKind.Null, plus.GetProperty("changedAt").ValueKind);
            Assert.Equal("$3.99/month", plus.GetProperty("priceLabel").GetString());
            Assert.Equal(Features.All.Count, plus.GetProperty("features").GetArrayLength());
            Assert.False(TierOf(view, "Pro").GetProperty("on").GetBoolean());
        }

        [Fact]
        public async Task FlippingTheSwitches_ChangesWhatEveryVisitorIsOffered_OnTheirNextRequest()
        {
            using var app = App(TempSiteFlags.NewPath());
            var client = Client(app);

            var view = await JsonAsync(await FlipAsync(client, "pro", true, Token()));
            Assert.Equal(SiteModes.PlusAndProAvailable, view.GetProperty("mode").GetString());
            Assert.NotEqual(JsonValueKind.Null, TierOf(view, "Pro").GetProperty("changedAt").ValueKind);
            Assert.Equal(SiteModes.PlusAndProAvailable, await ModeAsync(client));

            await JsonAsync(await FlipAsync(client, "plus", false, Token()));
            Assert.Equal(SiteModes.ProAvailable, await ModeAsync(client));

            await JsonAsync(await FlipAsync(client, "pro", false, Token()));
            Assert.Equal(SiteModes.FreeOnly, await ModeAsync(client));
        }

        [Fact]
        public async Task SwitchingPlusOff_DropsEveryoneHoldingThePlusCodeToFree()
        {
            using var app = App(TempSiteFlags.NewPath());
            var client = Client(app);
            using var unlocked = await client.PostAsync("/api/tier-access",
                new StringContent(JsonSerializer.Serialize(new { tier = "plus", code = PlusCode }), Encoding.UTF8, "application/json"));
            string cookie = unlocked.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

            await JsonAsync(await FlipAsync(client, "plus", false, Token()));

            using var me = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            me.Headers.Add("Cookie", cookie);
            Assert.Equal("Free", (await JsonAsync(await client.SendAsync(me))).GetProperty("tier").GetString());
        }

        [Fact]
        public async Task AFlip_IsKept_AcrossARestart()
        {
            string path = TempSiteFlags.NewPath();
            using (var first = App(path))
                await JsonAsync(await FlipAsync(Client(first), "pro", true, Token()));

            using var restarted = App(path);

            Assert.Equal(SiteModes.PlusAndProAvailable, await ModeAsync(Client(restarted)));
        }

        [Fact]
        public async Task AFlip_OverridesTheSwitchsDefaultFromConfiguration()
        {
            using var app = App(TempSiteFlags.NewPath(), plusDefault: false);
            var client = Client(app);
            Assert.Equal(SiteModes.FreeOnly, await ModeAsync(client));

            await JsonAsync(await FlipAsync(client, "plus", true, Token()));

            Assert.Equal(SiteModes.PlusAvailable, await ModeAsync(client));
        }

        [Theory]
        [InlineData("free")]
        [InlineData("gold")]
        public async Task OnlyPlusAndProHaveSwitches(string tier)
        {
            using var app = App(TempSiteFlags.NewPath());

            using var response = await FlipAsync(Client(app), tier, true, Token());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
