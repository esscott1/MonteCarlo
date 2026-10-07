using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MonteCarloSimulation.Web.Tests
{
    // The site's page layout: "/" lands on the splash page, titled Retirement Portfolio Explorer, with two tiles to the
    // Scenario runner (index.html) and the Optimizer (optimal.html), and a hamburger menu to Model Info, Translations and
    // Observe. Every tool page has a back arrow home; the Scenario runner keeps the change-request pencil.
    public class PagesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
    {
        private Task<string> GetAsync(string path) => factory.CreateClient().GetStringAsync(path);

        private static string Title(string html) => Regex.Match(html, "<title[^>]*>(.*?)</title>").Groups[1].Value;
        private static string Heading(string html) => Regex.Match(html, "<h1[^>]*>(.*?)</h1>").Groups[1].Value;
        private static bool HasId(string html, string id) => html.Contains($"id=\"{id}\"");

        [Fact]
        public async Task Root_IsTheSplashPage_WithTilesAndMenu()
        {
            string html = await GetAsync("/");

            Assert.Equal("Retirement Portfolio Explorer", Title(html));
            Assert.Equal("Retirement Portfolio Explorer", Heading(html));

            // Two tiles into the tools
            Assert.Matches(@"<a class=""splash-tile"" href=""index.html""", html);
            Assert.Matches(@"<a class=""splash-tile"" href=""optimal.html""", html);

            // The menu, in order: Model Info, Translations, Observe (the two tools are the tiles, not menu items)
            Assert.True(HasId(html, "hamburger-toggle"));
            Assert.True(HasId(html, "observe-flyout"));
            var menu = Regex.Match(html, @"<div id=""hamburger-menu""[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
            var items = Regex.Matches(menu, @"role=""menuitem""[^>]*>([^<]+)<").Select(m => m.Groups[1].Value);
            Assert.Equal(new[] { "Model Info", "Translations", "Observe" }, items);

            // This page is home, so no back arrow and no change-request pencil
            Assert.False(HasId(html, "edit-toggle"));
            Assert.DoesNotContain(@"aria-label=""Back to home""", html);

            // i18n.js loads before menu.js
            int i18nScript = html.IndexOf("<script src=\"i18n.js\"></script>");
            int menuScript = html.IndexOf("<script src=\"menu.js\"></script>");
            Assert.True(i18nScript >= 0 && menuScript > i18nScript);
        }

        [Fact]
        public async Task Root_AndSplashHtml_ServeTheSamePage()
        {
            Assert.Equal(await GetAsync("/splash.html"), await GetAsync("/"));
        }

        [Fact]
        public async Task OptimalHtml_IsTheOptimizer_WithABackArrow_AndNoMenu()
        {
            string html = await GetAsync("/optimal.html");

            Assert.Equal("Monte Carlo Portfolio Optimizer", Heading(html));
            Assert.True(HasId(html, "optimal-form"));
            Assert.Matches(@"<a href=""/"" class=""icon-button"" aria-label=""Back to home""[^>]*>", html);

            // The menu moved to the splash
            Assert.False(HasId(html, "hamburger-toggle"));
            Assert.False(HasId(html, "observe-flyout"));
        }

        [Fact]
        public async Task OptimalPage_InputsAreTabbedLikeTheScenarioRunner_BesideTheRecommendation()
        {
            string html = await GetAsync("/optimal.html");

            // The runner's four tabs, in order; the first is selected
            var tabs = Regex.Matches(html, @"role=""tab"" id=""(tab-\w+)"" aria-controls=""(panel-\w+)"" aria-selected=""(\w+)""");
            Assert.Equal(new[] { "tab-demographics", "tab-assets", "tab-money", "tab-income" }, tabs.Select(m => m.Groups[1].Value));
            Assert.Equal(new[] { "true", "false", "false", "false" }, tabs.Select(m => m.Groups[3].Value));

            string Panel(string id) => Regex.Match(html, $@"id=""{id}""(.*?)(?=<div class=""tab-panel|<section class=""graph-tile)", RegexOptions.Singleline).Groups[1].Value;
            Assert.All(new[] { "retirementDate", "birthdate", "years" }, name => Assert.Contains($@"name=""{name}""", Panel("panel-demographics")));
            Assert.All(new[] { "stockAllocation", "stockReturn", "stockStdDev", "bondAllocation", "bondReturn", "bondStdDev", "cashAllocation", "cashReturn", "cashStdDev", "stockBondCorrelation" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-assets")));
            Assert.All(new[] { "initialTaxableBalance", "enableRothConversions", "initialRothBasis", "initialRothUnrealizedGain", "initialBrokerageBasis", "initialBrokerageUnrealizedGain" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-money")));
            Assert.All(new[] { "socialSecurityAt62", "socialSecurityAt67", "socialSecurityAt70", "annualStandardDeduction", "newMoney", "yearNewMoney" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-income")));

            // The Optimizer finds the spend, so there's no withdrawal to enter; and there are no preset scenarios to pick
            Assert.DoesNotContain(@"name=""withdrawal""", html);
            Assert.DoesNotContain(@"name=""scenarioId""", html);

            // The recommendation fills the tile beside the inputs; the market count sits beside Find optimal, outside the tabs
            Assert.Matches(@"<section class=""graph-tile recommendation-tile""[^>]*>[\s\S]*?id=""optimal-card""", html);
            var submitRow = Regex.Match(html, @"<div class=""submit-row"">(.*?)\r?\n        </div>", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains(@"id=""optimal-submit""", submitRow);
            Assert.Contains(@"name=""paths""", submitRow);
            Assert.DoesNotContain(@"name=""paths""", Panel("panel-demographics") + Panel("panel-income"));

            // The shared inputs code loads before the page's own
            int inputsScript = html.IndexOf("<script src=\"inputs.js\"></script>");
            Assert.True(inputsScript >= 0 && html.IndexOf("<script src=\"optimal.js\"></script>") > inputsScript);
        }

        [Fact]
        public async Task IndexHtml_IsTheScenarioRunner_WithThePencilAndABackArrow()
        {
            string html = await GetAsync("/index.html");

            // The pencil's change requests exist to rename this page (the agent-title-change handler edits its <h1> and
            // <title>), so the test checks they're there without pinning their text
            Assert.False(string.IsNullOrWhiteSpace(Title(html)));
            Assert.False(string.IsNullOrWhiteSpace(Heading(html)));
            Assert.True(HasId(html, "run-form"));
            Assert.True(HasId(html, "edit-toggle"));
            Assert.True(HasId(html, "edit-flyout"));
            Assert.Matches(@"<a href=""/"" class=""icon-button"" aria-label=""Back to home""[^>]*>", html);

            // The menu moved to the landing page
            Assert.False(HasId(html, "hamburger-toggle"));
            Assert.False(HasId(html, "observe-flyout"));
        }

        [Fact]
        public async Task ScenarioRunner_InputsAreTabbed_BesideTheChartTile()
        {
            string html = await GetAsync("/index.html");

            // Four tabs, in order, each controlling its panel; the first is selected
            var tabs = Regex.Matches(html, @"role=""tab"" id=""(tab-\w+)"" aria-controls=""(panel-\w+)"" aria-selected=""(\w+)""");
            Assert.Equal(new[] { "tab-demographics", "tab-assets", "tab-money", "tab-income" }, tabs.Select(m => m.Groups[1].Value));
            Assert.Equal(new[] { "true", "false", "false", "false" }, tabs.Select(m => m.Groups[3].Value));
            foreach (Match tab in tabs)
                Assert.True(HasId(html, tab.Groups[2].Value));

            // Each input sits on its tab
            string Panel(string id) => Regex.Match(html, $@"id=""{id}""(.*?)(?=<div class=""tab-panel|<section class=""graph-tile)", RegexOptions.Singleline).Groups[1].Value;
            Assert.All(new[] { "retirementDate", "birthdate", "years", "iterations" }, name => Assert.Contains($@"name=""{name}""", Panel("panel-demographics")));
            Assert.All(new[] { "withdrawal", "initialTaxableBalance", "initialRothBasis", "initialRothUnrealizedGain", "initialBrokerageBasis", "initialBrokerageUnrealizedGain", "enableRothConversions" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-money")));
            Assert.All(new[] { "socialSecurityMonthlyAmount", "socialSecurityStartDate", "annualStandardDeduction", "newMoney", "yearNewMoney" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-income")));

            // Asset Classes: each class's allocation (60/30/10), return and std. dev. (8/19, 4.5/4, 3.5/1),
            // and the stock-bond correlation (0.1)
            string assets = Panel("panel-assets");
            var defaults = new Dictionary<string, string>
            {
                ["stockAllocation"] = "60", ["stockReturn"] = "8", ["stockStdDev"] = "19",
                ["bondAllocation"] = "30", ["bondReturn"] = "4.5", ["bondStdDev"] = "4",
                ["cashAllocation"] = "10", ["cashReturn"] = "3.5", ["cashStdDev"] = "1",
                ["stockBondCorrelation"] = "0.1",
            };
            foreach (var (name, value) in defaults)
                Assert.Matches($@"name=""{name}""[^>]*value=""{Regex.Escape(value)}""", assets);

            // One table row per class: its allocation, return and std. dev. side by side
            var rows = Regex.Matches(assets, @"<tr>(.*?)</tr>", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
            foreach (var asset in new[] { "stock", "bond", "cash" })
                Assert.Single(rows, row => new[] { "Allocation", "Return", "StdDev" }.All(field => row.Contains($@"name=""{asset}{field}""")));

            // The balance chart is the inputs' neighbour (chart.js draws it, loaded before app.js), and the preset radios are gone
            var graphTile = Regex.Match(html, @"<section class=""graph-tile""(.*?)</section>", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains(@"id=""balance-chart""", graphTile);
            int chartScript = html.IndexOf("<script src=\"chart.js\"></script>");
            Assert.True(chartScript >= 0 && html.IndexOf("<script src=\"app.js\"></script>") > chartScript);
            Assert.False(HasId(html, "scenario-options"));
        }

        [Theory]
        [InlineData("/optimal.html")]
        [InlineData("/model-info.html")]
        [InlineData("/observe.html")]
        [InlineData("/translations.html")]
        [InlineData("/access.html")]
        public async Task SubPages_GoBackToTheLandingPage(string path)
        {
            string html = await GetAsync(path);

            Assert.Matches(@"<a href=""/"" class=""icon-button"" aria-label=""Back to home""[^>]*>", html);
            Assert.DoesNotContain("href=\"index.html\"", html);
        }

        // English/Spanish: each translated page has the language button and loads i18n.js before its own script.
        // The Observe page stays English.
        [Theory]
        [InlineData("/", "menu.js")]
        [InlineData("/optimal.html", "optimal.js")]
        [InlineData("/index.html", "app.js")]
        [InlineData("/model-info.html", "model-info.js")]
        [InlineData("/access.html", "access.js")]
        public async Task TranslatedPages_HaveTheLanguageButton_AndLoadI18nFirst(string path, string pageScript)
        {
            string html = await GetAsync(path);

            Assert.Contains(@"<button type=""button"" class=""lang-toggle""", html);
            int i18nScript = html.IndexOf("<script src=\"i18n.js\"></script>");
            int ownScript = html.IndexOf($"<script src=\"{pageScript}\"></script>");
            Assert.True(i18nScript >= 0 && ownScript > i18nScript);
        }

        [Theory]
        [InlineData("/observe.html")]
        [InlineData("/translations.html")]
        public async Task ObserveAndTranslationsPages_StayInEnglish(string path)
        {
            string html = await GetAsync(path);

            Assert.DoesNotContain("lang-toggle", html);
            Assert.DoesNotContain("i18n.js", html);
        }

        [Fact]
        public async Task DeniedObserveVisit_ReturnsToTheLandingPage_WhereTheMenuReopensThePassphrase()
        {
            Assert.Contains("window.location.replace('/?observe=denied');", await GetAsync("/observe.js"));
            Assert.Contains("get('observe') === 'denied'", await GetAsync("/menu.js"));
            Assert.DoesNotContain("observe", await GetAsync("/app.js"));
        }

        // Paid tiers: every input a Free visitor can't change has a badge for its feature beside it, the features are the
        // server's, and paywall.js loads after i18n.js and the shared inputs code, before the page's own script
        [Theory]
        [InlineData("/index.html", "app.js")]
        [InlineData("/optimal.html", "optimal.js")]
        public async Task PaidFeatures_EachLockedInputHasABadge_AndPaywallLoadsBeforeThePage(string path, string pageScript)
        {
            string html = await GetAsync(path);

            var locked = Regex.Matches(html, @"data-requires=""([\w-]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
            var badges = Regex.Matches(html, @"<span class=""paid-badge"" data-feature=""([\w-]+)"" hidden></span>").Select(m => m.Groups[1].Value).ToHashSet();
            Assert.NotEmpty(locked);
            Assert.All(locked, feature => Assert.Contains(feature, badges));
            Assert.All(badges, feature => Assert.Contains(feature, Features.All));

            int i18n = html.IndexOf("<script src=\"i18n.js\"></script>");
            int inputs = html.IndexOf("<script src=\"inputs.js\"></script>");
            int paywall = html.IndexOf("<script src=\"paywall.js\"></script>");
            int page = html.IndexOf($"<script src=\"{pageScript}\"></script>");
            Assert.True(i18n >= 0 && inputs > i18n && paywall > inputs && page > paywall);
        }

        // The server holds a Free visitor's locked inputs to these values, and the pages lock them at their defaults
        [Theory]
        [InlineData("/index.html")]
        [InlineData("/optimal.html")]
        public async Task FreeValues_AreWhatThePagesLockTheInputsAt(string path)
        {
            string html = await GetAsync(path);
            var free = factory.Services.GetRequiredService<IOptions<FreeDefaultsOptions>>().Value;
            static string Percent(double fraction) => Math.Round(fraction * 100, 6).ToString("0.######", CultureInfo.InvariantCulture);

            Assert.Matches($@"name=""annualStandardDeduction""[^>]*value=""{free.StandardDeduction.ToString("N0", CultureInfo.InvariantCulture)}""[^>]*data-requires=""standard-deduction""", html);
            foreach (var (name, value) in new[]
            {
                ("stockReturn", Percent(AssetMixDefaults.StockReturn)), ("stockStdDev", Percent(AssetMixDefaults.StockStdDev)),
                ("bondReturn", Percent(AssetMixDefaults.BondReturn)), ("bondStdDev", Percent(AssetMixDefaults.BondStdDev)),
                ("cashReturn", Percent(AssetMixDefaults.CashReturn)), ("cashStdDev", Percent(AssetMixDefaults.CashStdDev)),
                ("stockBondCorrelation", AssetMixDefaults.StockBondCorrelation.ToString(CultureInfo.InvariantCulture)),
            })
                Assert.Matches($@"name=""{name}""[^>]*value=""{Regex.Escape(value)}""[^>]*data-requires=""custom-returns""", html);
            Assert.Matches(@"name=""enableRothConversions""[^>]*data-requires=""roth-conversions"" data-free-value=""false""", html);
            Assert.Matches(@"name=""newMoney""[^>]*data-requires=""inheritance"" data-free-value=""0""", html);
        }

        [Fact]
        public async Task PaidBadges_OpenTheAccessCodePageInANewTab()
        {
            string script = await GetAsync("/paywall.js");

            Assert.Contains("href=\"/billing/subscribe?tier=", script);
            Assert.Contains("target=\"_blank\" rel=\"noopener\"", script);
        }

        [Fact]
        public async Task OptimalResults_OfferRunInScenarioRunner_WhichOpensIndexHtml()
        {
            string script = await GetAsync("/optimal.js");

            // The button's text comes from the translations: t('optimal.runInScenarioRunner')
            Assert.Contains(">${t('optimal.runInScenarioRunner')}</button>", script);
            Assert.Contains(@"""optimal.runInScenarioRunner"": ""Run in Scenario runner""", await GetAsync("/i18n/en.json"));
            Assert.Contains("window.location.href = 'index.html';", script);
            Assert.DoesNotContain("Run in Simulator", script);
        }
    }
}
