using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.Web.Tests
{
    // Paid tiers behind access codes: GET /api/me says what a visitor may use, a tier's code earns a signed session cookie,
    // the site's Plus and Pro switches decide what's offered (both off: Free Only), and /api/run and /api/optimal refuse a
    // Free visitor's locked inputs. The switches here are their defaults (each test's own, in config), never flipped.
    public class TierAccessTests
    {
        private const string PlusCode = "plus-test-code";
        private const string ProCode = "pro-test-code";
        // appsettings.json gives Plus every feature but choosing the annuitant's sex, which is Pro's
        private static readonly string[] PlusFeatures = Features.All.Where(f => f != Features.AnnuityGender).ToArray();

        private static WebApplicationFactory<Program> App(
            bool tierPlus = true, bool tierPro = true, string plusCode = PlusCode, int? teaserYears = null) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("SiteFlags:Path", TempSiteFlags.NewPath());
                TestSpiaRates.Use(builder);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        ["FeatureManagement:TierPlus"] = tierPlus.ToString(),
                        ["FeatureManagement:TierPro"] = tierPro.ToString(),
                        ["Tiers:Plus:AccessCode"] = plusCode,
                        ["Tiers:Pro:AccessCode"] = ProCode,
                    };
                    if (teaserYears is int years) settings["FreeDefaults:TaxDetailTeaserYears"] = years.ToString();
                    config.AddInMemoryCollection(settings);
                });
            });

        // Cookies are passed by hand: the access cookie is Secure, and the test server is plain http
        private static HttpClient Client(WebApplicationFactory<Program> app) =>
            app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        private static StringContent Json(object body) =>
            new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static Task<HttpResponseMessage> EnterCodeAsync(HttpClient client, string tier, string code) =>
            client.PostAsync("/api/tier-access", Json(new { tier, code }));

        // The cookie a correct code sets, as a Cookie header value ("tier-access=...")
        private static async Task<string> UnlockAsync(HttpClient client, string tier, string code)
        {
            using var response = await EnterCodeAsync(client, tier, code);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        }

        private static async Task<JsonElement> MeAsync(HttpClient client, string? cookie = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response, HttpStatusCode status)
        {
            Assert.Equal(status, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("errors").Clone();
        }

        private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

        // --- What a visitor may use ---

        [Fact]
        public async Task FreeOnly_OffersNothing_RefusesEveryCode_AndLeavesEveryoneOnFree()
        {
            string plusCookie;
            using (var before = App())
                plusCookie = await UnlockAsync(Client(before), "plus", PlusCode);

            using var app = App(tierPlus: false, tierPro: false);
            var client = Client(app);

            var me = await MeAsync(client, plusCookie);
            Assert.Equal(SiteModes.FreeOnly, me.GetProperty("mode").GetString());
            Assert.Equal("Free", me.GetProperty("tier").GetString());
            Assert.Empty(Strings(me.GetProperty("features")));
            Assert.Equal(0, me.GetProperty("offers").GetArrayLength());
            foreach (var (tier, code) in new[] { ("plus", PlusCode), ("pro", ProCode) })
            {
                var errors = await ErrorsAsync(await EnterCodeAsync(client, tier, code), HttpStatusCode.BadRequest);
                Assert.True(errors.TryGetProperty("tier", out _));
            }
        }

        [Fact]
        public async Task ByDefault_TheSiteIsPlusAvailable()
        {
            using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                builder.UseSetting("SiteFlags:Path", TempSiteFlags.NewPath()));

            var me = await MeAsync(Client(app));

            Assert.Equal(SiteModes.PlusAvailable, me.GetProperty("mode").GetString());
            Assert.Equal(new[] { "Plus" }, me.GetProperty("offers").EnumerateArray().Select(o => o.GetProperty("tier").GetString()));
        }

        [Fact]
        public async Task AFreeVisitor_HasNoPaidFeatures_AndIsOfferedPlusThenPro()
        {
            using var app = App();

            var me = await MeAsync(Client(app));

            Assert.Equal(SiteModes.PlusAndProAvailable, me.GetProperty("mode").GetString());
            Assert.Equal("Free", me.GetProperty("tier").GetString());
            Assert.Empty(Strings(me.GetProperty("features")));
            var offers = me.GetProperty("offers").EnumerateArray().ToList();
            Assert.Equal(new[] { "Plus", "Pro" }, offers.Select(o => o.GetProperty("tier").GetString()));
            Assert.Equal(new[] { "$3.99/month", "$100/month" }, offers.Select(o => o.GetProperty("priceLabel").GetString()));
            Assert.All(offers, o => Assert.Equal("code", o.GetProperty("action").GetString()));
            Assert.Equal(PlusFeatures, Strings(offers[0].GetProperty("features")));
            Assert.Equal(Features.All, Strings(offers[1].GetProperty("features")));
            // What Pro will add, shown in its upgrade offer
            Assert.Empty(Strings(offers[0].GetProperty("highlights")));
            Assert.Equal(new[] { "Return models", "Per-account asset mixes", "Advisor workspace" }, Strings(offers[1].GetProperty("highlights")));
        }

        [Theory]
        [InlineData("plus", PlusCode, "Plus")]
        [InlineData("Pro", ProCode, "Pro")]
        public async Task ATiersCode_SetsASecureSessionCookie_ThatUnlocksItsFeatures(string tier, string code, string expected)
        {
            using var app = App();
            var client = Client(app);

            using var response = await EnterCodeAsync(client, tier, code);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string setCookie = response.Headers.GetValues("Set-Cookie").Single();
            string attributes = setCookie.ToLowerInvariant();
            Assert.StartsWith(TierAccessToken.CookieName + "=", setCookie);
            Assert.Contains("httponly", attributes);
            Assert.Contains("secure", attributes);
            Assert.Contains("samesite=lax", attributes);
            // A session cookie: no expiry date, so it's gone when the browser closes (the token inside lasts 12 hours)
            Assert.DoesNotContain("expires=", attributes);
            Assert.DoesNotContain("max-age=", attributes);

            var me = await MeAsync(client, setCookie.Split(';')[0]);
            Assert.Equal(expected, me.GetProperty("tier").GetString());
            Assert.Equal(expected == "Pro" ? Features.All : PlusFeatures, Strings(me.GetProperty("features")));
        }

        [Fact]
        public async Task AWrongCode_IsAFieldError_AndSetsNoCookie()
        {
            using var app = App();

            using var response = await EnterCodeAsync(Client(app), "plus", "not-the-code");

            Assert.False(response.Headers.Contains("Set-Cookie"));
            var errors = await ErrorsAsync(response, HttpStatusCode.BadRequest);
            Assert.Equal("Incorrect access code.", errors.GetProperty("code")[0].GetString());
        }

        [Fact]
        public async Task AnEmptyCode_AsksForOne()
        {
            using var app = App();

            var errors = await ErrorsAsync(await EnterCodeAsync(Client(app), "plus", " "), HttpStatusCode.BadRequest);

            Assert.Equal("Enter the access code.", errors.GetProperty("code")[0].GetString());
        }

        [Theory]
        [InlineData("free")]
        [InlineData("gold")]
        [InlineData("1")]
        [InlineData("")]
        public async Task ATierThatIsntSold_IsRefused(string tier)
        {
            using var app = App();

            var errors = await ErrorsAsync(await EnterCodeAsync(Client(app), tier, PlusCode), HttpStatusCode.BadRequest);

            Assert.Equal("That tier isn't available.", errors.GetProperty("tier")[0].GetString());
        }

        [Fact]
        public async Task WithProSwitchedOff_ProIsntOffered_ItsCodeIsRefused_AndAnEarlierProCookieCountsAsFree()
        {
            string proCookie;
            using (var before = App())
                proCookie = await UnlockAsync(Client(before), "pro", ProCode);

            using var app = App(tierPro: false);
            var client = Client(app);

            var errors = await ErrorsAsync(await EnterCodeAsync(client, "pro", ProCode), HttpStatusCode.BadRequest);
            Assert.True(errors.TryGetProperty("tier", out _));

            var me = await MeAsync(client, proCookie);
            Assert.Equal("Free", me.GetProperty("tier").GetString());
            Assert.Equal(new[] { "Plus" }, me.GetProperty("offers").EnumerateArray().Select(o => o.GetProperty("tier").GetString()));
        }

        [Fact]
        public async Task ProAvailable_OffersOnlyPro_RefusesThePlusCode_AndThePlusFeaturesCarryPro()
        {
            using var app = App(tierPlus: false);
            var client = Client(app);

            var me = await MeAsync(client);
            Assert.Equal(SiteModes.ProAvailable, me.GetProperty("mode").GetString());
            var offer = Assert.Single(me.GetProperty("offers").EnumerateArray());
            Assert.Equal("Pro", offer.GetProperty("tier").GetString());
            Assert.Equal(Features.All, Strings(offer.GetProperty("features")));

            Assert.True((await ErrorsAsync(await EnterCodeAsync(client, "plus", PlusCode), HttpStatusCode.BadRequest)).TryGetProperty("tier", out _));
            string proCookie = await UnlockAsync(client, "pro", ProCode);
            Assert.Equal(Features.All, Strings((await MeAsync(client, proCookie)).GetProperty("features")));
        }

        [Fact]
        public async Task ChangingATiersCode_SignsOutEveryoneWhoUsedTheOldOne()
        {
            string cookie;
            using (var before = App())
                cookie = await UnlockAsync(Client(before), "plus", PlusCode);

            using var app = App(plusCode: "a-new-plus-code");

            Assert.Equal("Free", (await MeAsync(Client(app), cookie)).GetProperty("tier").GetString());
        }

        [Fact]
        public async Task UsingTheFreeVersion_ClearsTheCookie()
        {
            using var app = App();

            using var response = await Client(app).DeleteAsync("/api/tier-access");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            string setCookie = response.Headers.GetValues("Set-Cookie").Single();
            Assert.StartsWith(TierAccessToken.CookieName + "=;", setCookie);
            Assert.Contains("expires=Thu, 01 Jan 1970", setCookie);
        }

        [Fact]
        public async Task CodeAttempts_AreLimitedTo20AnHourPerAddress()
        {
            using var app = App();
            var client = Client(app);

            for (int i = 0; i < 20; i++)
            {
                using var attempt = await EnterCodeAsync(client, "plus", "wrong");
                Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
            }
            using var refused = await EnterCodeAsync(client, "plus", PlusCode);

            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
            Assert.False(refused.Headers.Contains("Set-Cookie"));
        }

        [Theory]
        [InlineData("/billing/subscribe?tier=Plus", "/access.html?tier=plus")]
        [InlineData("/billing/subscribe?tier=pro", "/access.html?tier=pro")]
        [InlineData("/billing/subscribe?tier=free", "/access.html")]
        [InlineData("/billing/subscribe", "/access.html")]
        // The page the badge was on comes along, so the access page can bring the visitor back...
        [InlineData("/billing/subscribe?tier=plus&return=%2Findex.html", "/access.html?tier=plus&return=%2Findex.html")]
        [InlineData("/billing/subscribe?tier=pro&return=%2Foptimal.html%3Fx%3D1", "/access.html?tier=pro&return=%2Foptimal.html%3Fx%3D1")]
        [InlineData("/billing/subscribe?return=%2F", "/access.html?return=%2F")]
        // ...but only a page on this site, never another site or the access flow itself
        [InlineData("/billing/subscribe?tier=plus&return=https%3A%2F%2Fevil.example%2F", "/access.html?tier=plus")]
        [InlineData("/billing/subscribe?tier=plus&return=%2F%2Fevil.example", "/access.html?tier=plus")]
        [InlineData("/billing/subscribe?tier=plus&return=%2F%5Cevil.example", "/access.html?tier=plus")]
        [InlineData("/billing/subscribe?tier=plus&return=%2Faccess.html", "/access.html?tier=plus")]
        [InlineData("/billing/subscribe?tier=plus&return=%2Fbilling%2Fsubscribe", "/access.html?tier=plus")]
        public async Task TheBadgesSubscribeLink_GoesToTheAccessCodePage(string path, string location)
        {
            using var app = App();

            using var response = await Client(app).GetAsync(path);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(location, response.Headers.Location?.OriginalString);
        }

        [Theory]
        [InlineData("/", true)]
        [InlineData("/index.html", true)]
        [InlineData("/optimal.html?tab=money#results", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("index.html", false)]                 // not a path from the site's root
        [InlineData("https://evil.example/", false)]
        [InlineData("//evil.example", false)]             // a scheme-relative URL: another site
        [InlineData("/\\evil.example", false)]            // browsers read /\ like //
        [InlineData("/index.html\nSet-Cookie: x", false)] // no control characters
        [InlineData("/access.html?tier=plus", false)]     // the access flow itself would go in a circle
        [InlineData("/ACCESS.HTML", false)]
        [InlineData("/billing/subscribe", false)]
        public void ReturnPath_IsOnlyALocalPageOutsideTheAccessFlow(string? path, bool local)
        {
            Assert.Equal(local, ReturnPath.IsLocal(path));
        }

        [Fact]
        public void ReturnPath_RefusesAnOverlongPath()
        {
            Assert.True(ReturnPath.IsLocal("/" + new string('a', ReturnPath.MaxLength - 1)));
            Assert.False(ReturnPath.IsLocal("/" + new string('a', ReturnPath.MaxLength)));
        }

        // --- The locked inputs ---

        // The Scenario runner's defaults, but with a Free visitor's values for the locked inputs
        private static RunRequest FreeRunRequest() => new()
        {
            Years = 30,
            Iterations = 3,
            Withdrawal = 60_000,
            Birthdate = new DateOnly(1969, 7, 7),
            RetirementDate = new DateOnly(2027, 1, 1),
            InitialTaxableBalance = 1_000_000,
            InitialRothBasis = 20_000,
            InitialRothUnrealizedGain = 0,
            InitialBrokerageBasis = 200_000,
            InitialBrokerageUnrealizedGain = 200_000,
            NewMoney = 0,
            YearNewMoney = 10,
            SocialSecurityStartDate = new DateOnly(2031, 7, 7),
            SocialSecurityMonthlyAmount = 2_750,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = false,
            StockAllocation = 0.5,
            BondAllocation = 0.4,
            CashAllocation = 0.1,
            StockReturn = 0.08,
            StockStdDev = 0.19,
            BondReturn = 0.045,
            BondStdDev = 0.04,
            CashReturn = 0.035,
            CashStdDev = 0.01,
            StockBondCorrelation = 0.1
        };

        private static async Task<HttpResponseMessage> PostAsync<T>(WebApplicationFactory<Program> app, string path, T request, string? cookie = null)
        {
            var options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            using var message = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(JsonSerializer.Serialize(request, options), Encoding.UTF8, "application/json")
            };
            if (cookie is not null) message.Headers.Add("Cookie", cookie);
            return await Client(app).SendAsync(message);
        }

        [Fact]
        public async Task AFreeVisitor_RunsWithTheFreeValues()
        {
            using var app = App();

            using var response = await PostAsync(app, "/api/run", FreeRunRequest());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AFreeVisitor_SendingLockedInputs_GetsA403NamingEachOne()
        {
            using var app = App();
            var request = FreeRunRequest();
            request.EnableRothConversions = true;
            request.NewMoney = 500_000;
            request.AnnualStandardDeduction = 20_000;
            request.StockReturn = 0.09;
            request.StockBondCorrelation = 0.3;

            var errors = await ErrorsAsync(await PostAsync(app, "/api/run", request), HttpStatusCode.Forbidden);

            Assert.Equal("Roth conversions are a Plus feature.", errors.GetProperty("enableRothConversions")[0].GetString());
            Assert.Equal("Inheritance is a Plus feature.", errors.GetProperty("newMoney")[0].GetString());
            Assert.Equal("Changing the standard deduction is a Plus feature.", errors.GetProperty("annualStandardDeduction")[0].GetString());
            Assert.Equal(FreeTier.CustomReturnsMessage, errors.GetProperty("stockReturn")[0].GetString());
            Assert.Equal(FreeTier.CustomReturnsMessage, errors.GetProperty("stockBondCorrelation")[0].GetString());
            Assert.False(errors.TryGetProperty("bondReturn", out _));
        }

        // Every tier chooses its filing status; a Free visitor's standard deduction is held to that status's default
        [Theory]
        [InlineData("married", 32_000, true)]
        [InlineData("married", 16_000, false)]
        [InlineData("single", 32_000, false)]
        [InlineData(null, 16_000, true)]
        public async Task AFreeVisitorsStandardDeduction_FollowsTheFilingStatus(string? filingStatus, double deduction, bool allowed)
        {
            using var app = App();
            var request = FreeRunRequest();
            request.FilingStatus = filingStatus;
            request.AnnualStandardDeduction = deduction;

            using var response = await PostAsync(app, "/api/run", request);

            if (allowed) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            else
            {
                var errors = await ErrorsAsync(response, HttpStatusCode.Forbidden);
                Assert.Equal("Changing the standard deduction is a Plus feature.", errors.GetProperty("annualStandardDeduction")[0].GetString());
            }
        }

        [Fact]
        public async Task InvalidInput_IsStillA400_BeforeAnyTierCheck()
        {
            using var app = App();
            var request = FreeRunRequest();
            request.Years = 0;
            request.EnableRothConversions = true;

            var errors = await ErrorsAsync(await PostAsync(app, "/api/run", request), HttpStatusCode.BadRequest);

            Assert.True(errors.TryGetProperty("years", out _));
        }

        [Fact]
        public async Task APlusVisitor_MayChangeEveryInput()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);
            var request = FreeRunRequest();
            request.EnableRothConversions = true;
            request.NewMoney = 500_000;
            request.AnnualStandardDeduction = 20_000;
            request.StockReturn = 0.09;

            using var response = await PostAsync(app, "/api/run", request, cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // --- The Annuity tab: Plus, its COLA Pro ---

        // A DIA paying $2,600 a month from a little over two years out, with the default $400,000 basis
        private static RunRequest WithAnnuity(RunRequest request, bool cola = false)
        {
            request.IncludeAnnuity = true;
            request.AnnuityMonthlyAmount = 2_600;
            request.AnnuityInvestment = 400_000;
            request.AnnuityType = "dia";
            request.AnnuityStartDate = DateOnly.FromDateTime(DateTime.Today).AddYears(2).AddMonths(1);
            request.AnnuityCola = cola;
            return request;
        }

        [Fact]
        public async Task AFreeVisitor_SendingAnAnnuity_GetsA403()
        {
            using var app = App();

            var errors = await ErrorsAsync(await PostAsync(app, "/api/run", WithAnnuity(FreeRunRequest(), cola: true)), HttpStatusCode.Forbidden);

            Assert.Equal("Annuities are a Plus feature.", errors.GetProperty("includeAnnuity")[0].GetString());
            Assert.Equal("An annuity COLA is a Plus feature.", errors.GetProperty("annuityCola")[0].GetString());
        }

        [Fact]
        public async Task APlusVisitor_RunsAnAnnuity_AsIncome_AndItsCola_At2Percent()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);

            using var response = await PostAsync(app, "/api/run", WithAnnuity(FreeRunRequest()), cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var annuity = body.RootElement.GetProperty("parameters").GetProperty("annuity");
            Assert.Equal(400_000, annuity.GetProperty("basis").GetDouble());
            Assert.Equal(0, annuity.GetProperty("annualIncrease").GetDouble());
            var years = body.RootElement.GetProperty("output").GetProperty("result").GetProperty("runs")[0].GetProperty("years").EnumerateArray().ToList();
            Assert.Equal(0, years[0].GetProperty("annuityIncome").GetDouble());
            Assert.Contains(years, y => y.GetProperty("annuityPayments").GetInt32() == 12
                && Math.Abs(y.GetProperty("annuityIncome").GetDouble() - 31_200) < 1e-6);

            // The COLA is Plus too, at the app's one rate; the payment sent is the first year's (the page took its 10% off)
            using var withCola = await PostAsync(app, "/api/run", WithAnnuity(FreeRunRequest(), cola: true), cookie);
            Assert.Equal(HttpStatusCode.OK, withCola.StatusCode);
            using var colaBody = JsonDocument.Parse(await withCola.Content.ReadAsStringAsync());
            var colaAnnuity = colaBody.RootElement.GetProperty("parameters").GetProperty("annuity");
            Assert.Equal(MonteCarloSimulation.Core.Annuity.ColaIncrease, colaAnnuity.GetProperty("annualIncrease").GetDouble());
            Assert.Equal(2_600, colaAnnuity.GetProperty("monthlyPayment").GetDouble());
        }

        [Fact]
        public async Task AProVisitor_MayAddACola_InATaxDeferredAnnuity()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "pro", ProCode);
            var request = WithAnnuity(FreeRunRequest(), cola: true);
            request.AnnuityTaxDeferred = true;

            using var response = await PostAsync(app, "/api/run", request, cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var annuity = body.RootElement.GetProperty("parameters").GetProperty("annuity");
            Assert.Equal(0, annuity.GetProperty("basis").GetDouble()); // in a tax-deferred account: every payment taxed
            Assert.Equal(0.02, annuity.GetProperty("annualIncrease").GetDouble());
        }

        [Fact]
        public async Task ADiaStartingWithinTwoYears_IsA400_ButASpiaIgnoresTheDate()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);
            var tooSoon = WithAnnuity(FreeRunRequest());
            tooSoon.AnnuityStartDate = DateOnly.FromDateTime(DateTime.Today).AddYears(1);

            var errors = await ErrorsAsync(await PostAsync(app, "/api/run", tooSoon, cookie), HttpStatusCode.BadRequest);
            Assert.Equal("A DIA's payments must start at least 2 years from today.", errors.GetProperty("annuityStartDate")[0].GetString());

            tooSoon.AnnuityType = "spia";
            using var spia = await PostAsync(app, "/api/run", tooSoon, cookie);
            Assert.Equal(HttpStatusCode.OK, spia.StatusCode);
            using var body = JsonDocument.Parse(await spia.Content.ReadAsStringAsync());
            // A SPIA pays from the Retire On date
            Assert.Equal("2027-01-01", body.RootElement.GetProperty("parameters").GetProperty("annuity").GetProperty("firstPayment").GetString());
        }

        [Theory]
        [InlineData("annuityMonthlyAmount")]
        [InlineData("annuityInvestment")]
        [InlineData("annuityType")]
        public async Task AnAnnuitysInputs_AreValidated(string field)
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "pro", ProCode);
            var request = WithAnnuity(FreeRunRequest(), cola: true);
            switch (field)
            {
                case "annuityMonthlyAmount": request.AnnuityMonthlyAmount = 0; break;
                case "annuityInvestment": request.AnnuityInvestment = -1; break;
                case "annuityType": request.AnnuityType = "variable"; break;
            }

            var errors = await ErrorsAsync(await PostAsync(app, "/api/run", request, cookie), HttpStatusCode.BadRequest);

            Assert.True(errors.TryGetProperty(field, out _));
        }

        [Fact]
        public async Task InFreeOnly_EveryoneRunsTheFreeVersion()
        {
            using var app = App(tierPlus: false, tierPro: false);
            var request = FreeRunRequest();

            using var allowed = await PostAsync(app, "/api/run", request);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            using (var body = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync()))
                Assert.False(body.RootElement.GetProperty("taxDetail").GetBoolean());

            request.EnableRothConversions = true;
            var errors = await ErrorsAsync(await PostAsync(app, "/api/run", request), HttpStatusCode.Forbidden);
            Assert.True(errors.TryGetProperty("enableRothConversions", out _));
        }

        // --- The Free Scenario runner ---

        [Fact]
        public async Task Me_ReportsTheSharesAFreeVisitorsAccountTotalsAreSplitBy()
        {
            using var app = App();

            var shares = (await MeAsync(Client(app))).GetProperty("freeDefaults");

            Assert.Equal(0.5, shares.GetProperty("brokerageGainShare").GetDouble());
            Assert.Equal(1.0, shares.GetProperty("rothBasisShare").GetDouble());
        }

        [Fact]
        public async Task AFreeVisitorsAccounts_AreSplitByTheFreeShares_WhateverTheRequestSays()
        {
            using var app = App();
            var request = FreeRunRequest();
            request.InitialRothBasis = 15_000;
            request.InitialRothUnrealizedGain = 5_000;
            request.InitialBrokerageBasis = 100_000;
            request.InitialBrokerageUnrealizedGain = 300_000;

            using var response = await PostAsync(app, "/api/run", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var parameters = body.RootElement.GetProperty("parameters");
            Assert.Equal(20_000, parameters.GetProperty("initialRothBasis").GetDouble());
            Assert.Equal(0, parameters.GetProperty("initialRothUnrealizedGain").GetDouble());
            Assert.Equal(200_000, parameters.GetProperty("initialBrokerageBasis").GetDouble());
            Assert.Equal(200_000, parameters.GetProperty("initialBrokerageUnrealizedGain").GetDouble());
        }

        [Fact]
        public async Task APlusVisitorsAccounts_KeepTheirOwnSplit()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);
            var request = FreeRunRequest();
            request.InitialBrokerageBasis = 100_000;
            request.InitialBrokerageUnrealizedGain = 300_000;

            using var response = await PostAsync(app, "/api/run", request, cookie);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(100_000, body.RootElement.GetProperty("parameters").GetProperty("initialBrokerageBasis").GetDouble());
        }

        [Theory]
        [InlineData(null, 3)]   // the default
        [InlineData(1, 1)]
        public async Task AFreeVisitorsRun_KeepsTheTaxDetailForEachRunsFirstYears_AndOnlyTheTotalsAfter(int? setting, int teaserYears)
        {
            using var app = App(teaserYears: setting);

            using var response = await PostAsync(app, "/api/run", FreeRunRequest());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("taxDetail").GetBoolean());
            Assert.Equal(teaserYears, body.RootElement.GetProperty("taxDetailYears").GetInt32());
            var output = body.RootElement.GetProperty("output");
            var runs = output.GetProperty("result").GetProperty("runs").EnumerateArray().ToList();
            Assert.Equal(3, runs.Count);
            Assert.All(runs, run => Assert.False(run.TryGetProperty("lifetimeIrmaaSurcharges", out _)));

            var years = runs.SelectMany(run => run.GetProperty("years").EnumerateArray()).ToList();
            if (output.TryGetProperty("lastSuccessfulRun", out var last) && last.ValueKind == JsonValueKind.Array)
                years.AddRange(last.EnumerateArray());
            Assert.Contains(years, year => year.GetProperty("year").GetInt32() >= teaserYears);
            Assert.All(years, year =>
            {
                bool preview = year.GetProperty("year").GetInt32() < teaserYears;
                Assert.All(FreeRunView.TaxDetailFields, field => Assert.Equal(preview, year.TryGetProperty(field, out _)));
                Assert.All(new[] { "ordinaryTaxAmount", "capitalGainsTaxAmount", "taxRate", "withdrawal", "brokerageWithdrawal", "realizedGains", "balance",
                        "requiredMinimumDistribution", "rmdDivisor", "rmdSurplusToBrokerage" },
                    field => Assert.True(year.TryGetProperty(field, out _), field));
            });
        }

        [Fact]
        public async Task APlusVisitorsRun_HasTheFullTaxDetail()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);

            using var response = await PostAsync(app, "/api/run", FreeRunRequest(), cookie);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.TryGetProperty("taxDetail", out _));
            var year = body.RootElement.GetProperty("output").GetProperty("result").GetProperty("runs")[0].GetProperty("years")[0];
            Assert.All(FreeRunView.TaxDetailFields, field => Assert.True(year.TryGetProperty(field, out _), field));
        }

        // --- The Free Optimizer ---

        // The Optimal page's request with a Free visitor's values, its accounts already split by the Free shares
        private static OptimalRequest FreeOptimalRequest() => new()
        {
            Years = 30,
            Birthdate = new DateOnly(1969, 7, 7),
            RetirementDate = new DateOnly(2027, 1, 1),
            InitialTaxableBalance = 1_000_000,
            InitialRothBasis = 20_000,
            InitialBrokerageBasis = 200_000,
            InitialBrokerageUnrealizedGain = 200_000,
            SocialSecurityAt62 = 2_750,
            SocialSecurityAt67 = 3_900,
            SocialSecurityAt70 = 4_800,
            AnnualStandardDeduction = 16_000,
            StockAllocation = 0.6,
            BondAllocation = 0.3,
            CashAllocation = 0.1,
            StockReturn = 0.08,
            StockStdDev = 0.19,
            BondReturn = 0.045,
            BondStdDev = 0.04,
            CashReturn = 0.035,
            CashStdDev = 0.01,
            StockBondCorrelation = 0.1,
            Paths = 500,
        };

        private static async Task<List<JsonElement>> EventsAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();
        }

        [Fact]
        public async Task AFreeVisitorsOptimizer_Streams_ATeaser_From167Markets_AndSocialSecurityAt67AloneWithSsaRules()
        {
            using var app = App();

            var events = await EventsAsync(await PostAsync(app, "/api/optimal", FreeOptimalRequest()));

            var types = events.Select(e => e.GetProperty("type").GetString()).ToList();
            Assert.Equal("start", types[0]);
            Assert.Equal(new[] { "teaser", "done" }, types.TakeLast(2));
            Assert.DoesNotContain("result", types);
            Assert.Equal(167, events[0].GetProperty("paths").GetInt32());

            // What the server ran: the entered 62 and 70 amounts replaced by SSA's 70% and 124% of the amount at 67
            var expected = FreeOptimalRequest();
            expected.SocialSecurityAt62 = 3_900 * SocialSecurityCurve.SsaShareAt62;
            expected.SocialSecurityAt70 = 3_900 * SocialSecurityCurve.SsaShareAt70;
            expected.Paths = 167;
            var optimum = SpendingOptimizer.Optimize(expected.ToInputs()).Optimum;
            var teaser = OptimalTeaserEvent.For(optimum.Recommended.SpendAtMidpoint);
            Assert.Equal(teaser.MonthlyLow, events[^2].GetProperty("monthlyLow").GetDouble());
            Assert.Equal(teaser.MonthlyHigh, events[^2].GetProperty("monthlyHigh").GetDouble());
            Assert.True(teaser.MonthlyLow <= optimum.Recommended.SpendAtMidpoint / 12 && optimum.Recommended.SpendAtMidpoint / 12 < teaser.MonthlyHigh);

            // While Plus offers annuities, the teaser also weighs buying one (a man's rate) and says what it might add -
            // the Optimizer's own gain, rounded down to $100 - and nothing else about it
            var withPurchase = SpendingOptimizer.Optimize(expected.ToInputs(await TestSpiaRates.PurchaseFor(app, expected))).Optimum;
            var expectedTeaser = OptimalTeaserEvent.For(withPurchase);
            Assert.Equal(SpendingOptimizer.ClaimingAges.Count * 5 + 1, events[0].GetProperty("totalSteps").GetInt32());
            Assert.NotNull(expectedTeaser.AnnuityGainMonthly);
            Assert.Equal(expectedTeaser.AnnuityGainMonthly!.Value, events[^2].GetProperty("annuityGainMonthly").GetDouble());
            Assert.Equal(teaser.MonthlyLow, expectedTeaser.MonthlyLow);
            Assert.Equal(new[] { "annuityGainMonthly", "monthlyHigh", "monthlyLow", "type" }, events[^2].EnumerateObject().Select(p => p.Name).Order());
        }

        [Fact]
        public async Task APlusVisitorsOptimizer_StreamsTheFullResult_WithTheirOwnMarketsAndAmounts()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);
            var request = FreeOptimalRequest();
            request.Paths = 167;

            var events = await EventsAsync(await PostAsync(app, "/api/optimal", request, cookie));

            Assert.Equal("result", events[^2].GetProperty("type").GetString());
            // With the annuity feature the Optimizer also weighs buying an annuity, at the app's current rate
            var purchase = await TestSpiaRates.PurchaseFor(app, request);
            Assert.NotNull(purchase);
            Assert.Equal(JsonSerializer.Serialize(SpendingOptimizer.Optimize(request.ToInputs(purchase)).Optimum,
                    app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions),
                events[^2].GetProperty("optimum").GetRawText());
        }

        // The Scenario runner's Optimize: a Free visitor gets the teaser too (its range), and locked inputs are refused first
        [Fact]
        public async Task AFreeVisitorsRunnerOptimize_Streams_ATeaser_AndRefusesLockedInputs()
        {
            using var app = App();

            var events = await EventsAsync(await PostAsync(app, "/api/run/optimize", FreeRunRequest()));

            var types = events.Select(e => e.GetProperty("type").GetString()).ToList();
            Assert.Equal(new[] { "teaser", "done" }, types.TakeLast(2));
            Assert.DoesNotContain("result", types);
            Assert.Equal(167, events[0].GetProperty("paths").GetInt32());
            Assert.True(events[^2].GetProperty("monthlyLow").GetDouble() > 0);

            var locked = FreeRunRequest();
            locked.EnableRothConversions = true;
            var errors = await ErrorsAsync(await PostAsync(app, "/api/run/optimize", locked), HttpStatusCode.Forbidden);
            Assert.Equal("Roth conversions are a Plus feature.", errors.GetProperty("enableRothConversions")[0].GetString());
        }

        // The runner's Optimize Social Security is the full Optimizer's choice of claiming age: Plus
        [Fact]
        public async Task OptimizeSocialSecurity_IsPlus()
        {
            using var app = App();

            // The runner's own inputs, valid as they stand (the three estimates left at 0)
            var errors = await ErrorsAsync(await PostAsync(app, "/api/run/optimize-social-security", FreeRunRequest()), HttpStatusCode.Forbidden);

            Assert.Equal("Optimizing Social Security is a Plus feature.", errors.GetProperty("socialSecurity")[0].GetString());
        }

        // --- The Optimizer's suggested annuity: Plus; the annuitant's gender for its rate, Pro ---

        [Fact]
        public async Task InFreeOnly_TheTeaserWeighsNoAnnuity()
        {
            using var app = App(tierPlus: false, tierPro: false);

            var events = await EventsAsync(await PostAsync(app, "/api/optimal", FreeOptimalRequest()));

            Assert.Equal(SpendingOptimizer.ClaimingAges.Count, events[0].GetProperty("totalSteps").GetInt32());
            Assert.Equal(JsonValueKind.Null, events[^2].GetProperty("annuityGainMonthly").ValueKind);
        }

        [Fact]
        public async Task APlusVisitorWhoUnticksConsiderBuyingAnAnnuity_GetsThePlainSearch()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);
            var request = FreeOptimalRequest();
            request.Paths = 167;
            request.ConsiderAnnuities = false;

            var events = await EventsAsync(await PostAsync(app, "/api/optimal", request, cookie));

            Assert.Equal(SpendingOptimizer.ClaimingAges.Count, events[0].GetProperty("totalSteps").GetInt32());
            var optimum = events[^2].GetProperty("optimum");
            Assert.Equal(JsonValueKind.Null, optimum.GetProperty("purchase").ValueKind);
            Assert.Equal(JsonSerializer.Serialize(SpendingOptimizer.Optimize(request.ToInputs()).Optimum,
                    app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions),
                optimum.GetRawText());
        }

        [Fact]
        public async Task APlusVisitorsOptimizer_SaysWhatItDecidedAboutBuyingAnAnnuity()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);
            var request = FreeOptimalRequest();
            request.Paths = 167;

            var events = await EventsAsync(await PostAsync(app, "/api/optimal", request, cookie));

            var decision = events[^2].GetProperty("optimum").GetProperty("purchase");
            Assert.Equal(JsonValueKind.Object, decision.ValueKind);
            // $100,000 steps of $1,000,000 Tax Deferred and of what Brokerage nets plus Roth, each level and with the COLA
            var options = await TestSpiaRates.PurchaseFor(app, request);
            Assert.Equal(20 + 2 * AnnuityPurchase.AfterTaxAmounts(request.ToInputs().Template, options!).Count, decision.GetProperty("amountsConsidered").GetInt32());
            Assert.True(decision.GetProperty("amountsConsidered").GetInt32() > 20);
            Assert.False(string.IsNullOrEmpty(decision.GetProperty("rateAsOf").GetString()));
            Assert.Equal(options!.MonthlyPer100k, decision.GetProperty("monthlyPer100k").GetDouble());
        }

        [Fact]
        public async Task OnlyPro_MayAskForAWomansRate_AndFreeVisitorsChoiceIsIgnored()
        {
            using var app = App();
            var client = Client(app);
            string plus = await UnlockAsync(client, "plus", PlusCode);
            var request = FreeOptimalRequest();
            request.Paths = 167;
            request.AnnuitySex = "female";

            var errors = await ErrorsAsync(await PostAsync(app, "/api/optimal", request, plus), HttpStatusCode.Forbidden);
            Assert.Equal("Choosing the annuitant's gender is a Pro feature.", errors.GetProperty("annuitySex")[0].GetString());

            string pro = await UnlockAsync(client, "pro", ProCode);
            var events = await EventsAsync(await PostAsync(app, "/api/optimal", request, pro));
            double woman = events[^2].GetProperty("optimum").GetProperty("purchase").GetProperty("monthlyPer100k").GetDouble();
            request.AnnuitySex = "male";
            Assert.True(woman < (await TestSpiaRates.PurchaseFor(app, request))!.MonthlyPer100k);

            // Free runs no purchase search, so the choice doesn't matter there
            var free = FreeOptimalRequest();
            free.AnnuitySex = "female";
            var teaser = await EventsAsync(await PostAsync(app, "/api/optimal", free));
            Assert.Equal("teaser", teaser[^2].GetProperty("type").GetString());
        }

        [Fact]
        public async Task AnUnknownAnnuitantGender_IsA400()
        {
            using var app = App();
            string cookie = await UnlockAsync(Client(app), "plus", PlusCode);
            var request = FreeOptimalRequest();
            request.AnnuitySex = "other";

            var errors = await ErrorsAsync(await PostAsync(app, "/api/optimal", request, cookie), HttpStatusCode.BadRequest);

            Assert.True(errors.TryGetProperty("annuitySex", out _));
        }

        private static SpendOptimum Decided(double spendWithout, double spendWith, double amount)
        {
            var age = new ClaimingAgeResult(67, new DateOnly(2036, 7, 7), 3_900, 0, spendWith, spendWith, spendWith,
                Core.WithdrawalStrategy.TaxOptimized, Core.RothConversionTarget.Bracket12);
            var decision = new AnnuityPurchaseDecision(amount, amount * 0.00612, 612, "2026-10-01", spendWithout, spendWith, 2e6, 1e6, 10);
            return new SpendOptimum(age, 0.825, [age], decision);
        }

        [Theory]
        [InlineData(96_000, 103_320, 400_000, 600.0)]   // $610 a month more: about $600
        [InlineData(96_000, 97_000, 400_000, null)]     // $83 a month more: under $100, no figure
        [InlineData(96_000, 96_000, 0, null)]           // none bought
        public void TheFreeTeasersAnnuityGain_IsRoundedDownTo100_AndItsRangeIsThePlanWithoutAPurchase(
            double without, double with, double amount, double? gain)
        {
            var teaser = OptimalTeaserEvent.For(Decided(without, with, amount));

            Assert.Equal(gain, teaser.AnnuityGainMonthly);
            Assert.Equal(8_000, teaser.MonthlyLow); // $96,000 a year without the purchase
            Assert.Equal(8_500, teaser.MonthlyHigh);
        }

        [Theory]
        [InlineData(72_900, 6_000, 6_500)]   // $6,075 a month
        [InlineData(72_000, 6_000, 6_500)]   // exactly $6,000
        [InlineData(5_000, 0, 500)]
        public void TheTeaser_IsTheMonthlySpendsRangeOf500(double annual, double low, double high)
        {
            var teaser = OptimalTeaserEvent.For(annual);

            Assert.Equal(low, teaser.MonthlyLow);
            Assert.Equal(high, teaser.MonthlyHigh);
        }

        [Fact]
        public async Task TheOptimizer_RefusesAFreeVisitorsLockedInputs_BeforeStreaming()
        {
            using var app = App();
            var request = new OptimalRequest
            {
                Years = 30,
                Birthdate = new DateOnly(1969, 7, 7),
                RetirementDate = new DateOnly(2027, 1, 1),
                InitialTaxableBalance = 1_000_000,
                InitialBrokerageBasis = 200_000,
                InitialBrokerageUnrealizedGain = 200_000,
                SocialSecurityAt62 = 2_730,
                SocialSecurityAt67 = 3_900,
                SocialSecurityAt70 = 4_836,
                AnnualStandardDeduction = 16_000,
                EnableRothConversions = true,
                NewMoney = 1_000_000,
                YearNewMoney = 10,
                StockAllocation = 0.6,
                BondAllocation = 0.3,
                CashAllocation = 0.1,
                StockReturn = 0.08,
                StockStdDev = 0.19,
                BondReturn = 0.045,
                BondStdDev = 0.04,
                CashReturn = 0.035,
                CashStdDev = 0.01,
                StockBondCorrelation = 0.1,
            };

            var errors = await ErrorsAsync(await PostAsync(app, "/api/optimal", request), HttpStatusCode.Forbidden);

            Assert.True(errors.TryGetProperty("enableRothConversions", out _));
            Assert.True(errors.TryGetProperty("newMoney", out _));
        }
    }

    // The access cookie's token on its own: stateless, signed with the tier's code, good for 12 hours.
    public class TierAccessTokenTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        private static string? Codes(Tier tier) => tier switch { Tier.Plus => "plus", Tier.Pro => "pro", _ => null };

        [Theory]
        [InlineData(Tier.Plus)]
        [InlineData(Tier.Pro)]
        public void ATokenGrantsItsTier_For12Hours(Tier tier)
        {
            string token = TierAccessToken.Issue(tier, Codes(tier)!, Now);

            Assert.Equal(tier, TierAccessToken.Read(token, Codes, Now));
            Assert.Equal(tier, TierAccessToken.Read(token, Codes, Now.AddHours(12)));
            Assert.Equal(Tier.Free, TierAccessToken.Read(token, Codes, Now.AddHours(12).AddSeconds(1)));
        }

        [Fact]
        public void EditingTheTier_BreaksTheSignature()
        {
            string token = TierAccessToken.Issue(Tier.Plus, "same", Now);

            Assert.Equal(Tier.Free, TierAccessToken.Read("Pro" + token["Plus".Length..], _ => "same", Now));
        }

        [Fact]
        public void ANewCode_InvalidatesTokensIssuedWithTheOldOne()
        {
            string token = TierAccessToken.Issue(Tier.Plus, "old", Now);

            Assert.Equal(Tier.Free, TierAccessToken.Read(token, _ => "new", Now));
            Assert.Equal(Tier.Free, TierAccessToken.Read(token, _ => null, Now));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Plus")]
        [InlineData("Plus.notanumber.abc")]
        [InlineData("Plus.99999999999.not-base64!")]
        [InlineData("Free.99999999999.AAAA")]
        [InlineData("1.99999999999.AAAA")]
        public void AMalformedToken_IsFree(string? token)
        {
            Assert.Equal(Tier.Free, TierAccessToken.Read(token, Codes, Now));
        }
    }
}
