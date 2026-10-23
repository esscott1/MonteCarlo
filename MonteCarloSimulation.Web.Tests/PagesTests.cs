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

        [Theory]
        [InlineData("/")]
        [InlineData("/index.html")]
        [InlineData("/inputs.js")]
        [InlineData("/app.js")]
        [InlineData("/styles.css")]
        [InlineData("/i18n/es.json")]
        public async Task PageFiles_MustBeRevalidated_SoADeployNeverMixesOldAndNewScripts(string path)
        {
            var client = factory.CreateClient();
            using var response = await client.GetAsync(path);

            Assert.True(response.Headers.CacheControl?.NoCache, $"{path} has no Cache-Control: no-cache");

            // Checking again costs a 304 when nothing changed
            using var again = new HttpRequestMessage(HttpMethod.Get, path);
            again.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var revalidated = await client.SendAsync(again);
            Assert.Equal(System.Net.HttpStatusCode.NotModified, revalidated.StatusCode);
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

            // The runner's five tabs, in order; the first is selected
            var tabs = Regex.Matches(html, @"role=""tab"" id=""(tab-\w+)"" aria-controls=""(panel-\w+)"" aria-selected=""(\w+)""");
            Assert.Equal(new[] { "tab-demographics", "tab-assets", "tab-money", "tab-income", "tab-annuity" }, tabs.Select(m => m.Groups[1].Value));
            Assert.Equal(new[] { "true", "false", "false", "false", "false" }, tabs.Select(m => m.Groups[3].Value));
            // The Annuity tab is Plus: a pill in its label, only to look at (the tab is already a button); its text in a
            // span, so switching language doesn't replace the pill
            Assert.Matches(@"id=""tab-annuity""[^>]*><span data-i18n=""runner.tab.annuity"">[^<]*</span> <span class=""paid-badge tab-badge"" data-feature=""annuity"" data-display-only hidden></span></button>", html);

            string Panel(string id) => Regex.Match(html, $@"id=""{id}""(.*?)(?=<div class=""tab-panel|<section class=""graph-tile)", RegexOptions.Singleline).Groups[1].Value;
            Assert.All(new[] { "retirementDate", "birthdate", "years" }, name => Assert.Contains($@"name=""{name}""", Panel("panel-demographics")));
            Assert.All(new[] { "stockAllocation", "stockReturn", "stockStdDev", "bondAllocation", "bondReturn", "bondStdDev", "cashAllocation", "cashReturn", "cashStdDev", "stockBondCorrelation" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-assets")));
            Assert.All(new[] { "initialTaxableBalance", "enableRothConversions", "initialRothBasis", "initialRothUnrealizedGain", "initialBrokerageBasis", "initialBrokerageUnrealizedGain" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-money")));
            Assert.All(new[] { "socialSecurityAt62", "socialSecurityAt67", "socialSecurityAt70", "annualStandardDeduction", "newMoney", "yearNewMoney" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-income")));

            // The Annuity tab only shows what the Optimizer decided: nothing to enter (every field read-only or disabled)
            // but whether to consider a purchase and the Pro gender choice, which sets the rate it buys at
            string annuity = Regex.Match(html, @"id=""panel-annuity""(.*?)(?=<section class=""graph-tile)", RegexOptions.Singleline).Groups[1].Value;
            Assert.Matches(@"name=""annuityInvestment""[^>]*readonly", annuity);
            Assert.Matches(@"name=""annuityMonthlyAmount""[^>]*readonly", annuity);
            Assert.Matches(@"name=""annuityType"" value=""spia"" checked disabled", annuity);
            Assert.Matches(@"name=""annuityType"" value=""dia"" disabled", annuity);
            Assert.Matches(@"name=""annuityTaxDeferred"" checked disabled", annuity);
            Assert.DoesNotContain("includeAnnuity", annuity);
            Assert.DoesNotContain("annuityCola", annuity);
            // Whether what it bought has the app's COLA (it weighs each size with and without one): shown, never entered
            Assert.Matches(@"<input type=""checkbox"" name=""boughtCola"" disabled> <span data-i18n=""runner.annuity.cola"">[^<]*</span> <small class=""cola-rate"" data-i18n=""runner.annuity.colaRateNote"">2% a year</small> <span class=""paid-badge"" data-feature=""annuity-cola"" hidden></span>", annuity);
            Assert.Equal(new[] { "annuity", "annuity-gender", "annuity-gender" }, Regex.Matches(annuity, @"data-requires=""([\w-]+)""").Select(m => m.Groups[1].Value));
            // "Consider buying an annuity" (Plus): ticked, and held ticked for Free, whose teaser always weighs one
            Assert.Matches(@"<input type=""checkbox"" name=""considerAnnuities"" checked data-requires=""annuity"" data-free-value=""true"">", annuity);
            Assert.Contains(@"<span class=""paid-badge"" data-feature=""annuity"" hidden></span>", annuity);
            Assert.Contains(@"id=""td-purchase-note"" hidden", Panel("panel-money"));
            // An annuity bought with after-tax money comes out of Brokerage, then Roth: each says what it paid
            Assert.Contains(@"id=""brokerage-purchase-note"" hidden", Panel("panel-money"));
            Assert.Contains(@"id=""roth-purchase-note"" hidden", Panel("panel-money"));

            // The Optimizer finds the spend, so there's no withdrawal to enter; and there are no preset scenarios to pick
            Assert.DoesNotContain(@"name=""withdrawal""", html);
            Assert.DoesNotContain(@"name=""scenarioId""", html);

            // The recommendation fills the tile beside the inputs; the market count sits beside Find optimal, outside the tabs
            Assert.Matches(@"<section class=""graph-tile recommendation-tile""[^>]*>[\s\S]*?id=""optimal-card""", html);
            var submitRow = Regex.Match(html, @"<div class=""submit-row"">(.*?)\r?\n        </div>", RegexOptions.Singleline).Groups[1].Value;
            Assert.Contains(@"id=""optimal-submit""", submitRow);
            Assert.Contains(@"name=""paths""", submitRow);
            Assert.DoesNotContain(@"name=""paths""", Panel("panel-demographics") + Panel("panel-income"));
            // ... above the foldable inputs, so it's in view on every tab and when they're folded
            Assert.True(html.IndexOf(@"<div class=""submit-row"">", StringComparison.Ordinal) < html.IndexOf(@"<details id=""inputs-section""", StringComparison.Ordinal));

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

            // Five tabs, in order, each controlling its panel; the first is selected
            var tabs = Regex.Matches(html, @"role=""tab"" id=""(tab-\w+)"" aria-controls=""(panel-\w+)"" aria-selected=""(\w+)""");
            Assert.Equal(new[] { "tab-demographics", "tab-assets", "tab-money", "tab-income", "tab-annuity" }, tabs.Select(m => m.Groups[1].Value));
            Assert.Equal(new[] { "true", "false", "false", "false", "false" }, tabs.Select(m => m.Groups[3].Value));
            // The Annuity tab is Plus: a pill in its label, only to look at (the tab is already a button); its text in a
            // span, so switching language doesn't replace the pill
            Assert.Matches(@"id=""tab-annuity""[^>]*><span data-i18n=""runner.tab.annuity"">[^<]*</span> <span class=""paid-badge tab-badge"" data-feature=""annuity"" data-display-only hidden></span></button>", html);
            foreach (Match tab in tabs)
                Assert.True(HasId(html, tab.Groups[2].Value));

            // The Monthly Withdrawal and Run sit above the foldable inputs, on no tab, so they stay in view on every tab and
            // when the inputs are folded
            string aboveInputs = html[html.IndexOf(@"<form id=""run-form"">", StringComparison.Ordinal)..html.IndexOf(@"<details id=""inputs-section""", StringComparison.Ordinal)];
            Assert.Contains(@"name=""withdrawal""", aboveInputs);
            Assert.Single(Regex.Matches(html, @"name=""withdrawal"""));
            Assert.Contains(@"<button type=""submit"" data-i18n=""runner.submit"">", aboveInputs);
            // ... with the run's progress and then See results right of Run (app.js fills it)
            Assert.Matches(@"runner.submit"">Run</button>\s*<span id=""run-status"" class=""run-status"" aria-live=""polite""></span>", aboveInputs);
            Assert.Single(Regex.Matches(html, @"data-i18n=""runner.submit"""));

            // Each other input sits on its tab
            string Panel(string id) => Regex.Match(html, $@"id=""{id}""(.*?)(?=<div class=""tab-panel|<section class=""graph-tile)", RegexOptions.Singleline).Groups[1].Value;
            Assert.All(new[] { "retirementDate", "birthdate", "years", "iterations" }, name => Assert.Contains($@"name=""{name}""", Panel("panel-demographics")));
            Assert.All(new[] { "initialTaxableBalance", "initialRothBasis", "initialRothUnrealizedGain", "initialBrokerageBasis", "initialBrokerageUnrealizedGain", "enableRothConversions" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-money")));
            Assert.All(new[] { "socialSecurityMonthlyAmount", "socialSecurityStartDate", "annualStandardDeduction", "newMoney", "yearNewMoney" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-income")));
            Assert.All(new[] { "includeAnnuity", "annuityMonthlyAmount", "annuityInvestment", "annuityType", "annuityStartDate", "annuityTaxDeferred", "annuityCola", "annuitySex" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-annuity")));

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

        // Observe's sections each collapse (a <details> whose summary is the heading). Features comes first: the site-wide
        // Plus and Pro switches, read and saved with the token; then Visits, the visits per day, read with the token.
        [Fact]
        public async Task ObservePage_HasTheFeaturesSectionFirst_WithItsSwitchesBehindTheToken()
        {
            string html = await GetAsync("/observe.html");
            string script = await GetAsync("/observe.js");

            var sections = Regex.Matches(html, @"<details class=""observe-section""[^>]*>\s*<summary><h2[^>]*>([^<]+)</h2></summary>").Select(m => m.Groups[1].Value);
            Assert.Equal(new[] { "Features", "Visits", "Logs", "Errors", "Open Jira Items" }, sections);
            Assert.DoesNotContain("<section", html);
            Assert.Contains("'/api/visits'", script);
            Assert.Contains("'/api/features'", script);
            Assert.Contains("`/api/features/${input.dataset.tier}`", script);
            Assert.Contains("'X-Observe-Token': token", script);
            Assert.Contains(@"role=""switch""", script);
        }

        // Visits are counted from the public pages only, so Observe and Translations (the owner's own pages) don't add any
        [Theory]
        [InlineData("/splash.html", true)]
        [InlineData("/index.html", true)]
        [InlineData("/optimal.html", true)]
        [InlineData("/model-info.html", true)]
        [InlineData("/access.html", true)]
        [InlineData("/observe.html", false)]
        [InlineData("/translations.html", false)]
        public async Task PublicPages_CountAVisit_TheOwnersPagesDont(string path, bool counts)
        {
            string html = await GetAsync(path);

            Assert.Equal(counts, html.Contains(@"<script src=""visit.js"" defer></script>"));
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
            if (path == "/index.html")
            {
                Assert.Matches(@"name=""includeAnnuity""[^>]*data-requires=""annuity"" data-free-value=""false""", html);
                Assert.Matches(@"name=""annuityCola""[^>]*data-requires=""annuity-cola"" data-free-value=""false""", html);
            }
            // Both pages: the annuitant's gender (Pro) locks at Male
            Assert.Matches(@"name=""annuitySex"" value=""male"" checked data-requires=""annuity-gender""", html);
            // A married household's default (inputs.js moves the field to it when Married is chosen)
            Assert.Matches($@"name=""annualStandardDeduction""[^>]*data-married-value=""{free.StandardDeductionMarried.ToString("N0", CultureInfo.InvariantCulture)}""", html);
        }

        // Light/dark: every page applies a saved choice from <head> (theme.js, right after the stylesheet, before it draws),
        // and the pages with a language button have the toggle right after it
        [Theory]
        [InlineData("/splash.html", true)]
        [InlineData("/index.html", true)]
        [InlineData("/optimal.html", true)]
        [InlineData("/model-info.html", true)]
        [InlineData("/access.html", true)]
        [InlineData("/observe.html", false)]
        [InlineData("/translations.html", false)]
        public async Task EveryPage_FollowsTheChosenTheme(string path, bool hasToggle)
        {
            string html = await GetAsync(path);
            string head = Regex.Match(html, @"<head>(.*?)</head>", RegexOptions.Singleline).Groups[1].Value;

            Assert.Matches(@"<link rel=""stylesheet"" href=""styles.css"" />\s*<script src=""theme.js""></script>", head);
            if (hasToggle)
                Assert.Matches(@"<button type=""button"" class=""lang-toggle""[^>]*>[^<]*</button>\s*<button type=""button"" class=""theme-toggle""", html);
            else
                Assert.DoesNotContain("theme-toggle", html);
        }

        // The dark colours apply twice - under the computer's dark setting unless the toggle chose light, and when it
        // chose dark - so each pair must declare exactly the same colours
        [Fact]
        public async Task DarkColours_AreTheSameForTheSettingAndTheToggle()
        {
            string css = await GetAsync("/styles.css");
            static string[] Declarations(string body) => body.Split(';').Select(d => Regex.Replace(d, @"\s+", " ").Trim()).Where(d => d.Length > 0).ToArray();

            var setting = Regex.Matches(css, @"@media \(prefers-color-scheme: dark\) \{\s*:root(\S*) \{([^}]*)\}\s*\}");
            var toggle = Regex.Matches(css, @"\n:root\[data-theme=""dark""\] \{([^}]*)\}");

            Assert.Equal(2, setting.Count);
            Assert.Equal(setting.Count, toggle.Count);
            for (int i = 0; i < setting.Count; i++)
            {
                Assert.Equal(@":not([data-theme=""light""])", setting[i].Groups[1].Value);
                Assert.Equal(Declarations(setting[i].Groups[2].Value), Declarations(toggle[i].Groups[1].Value));
            }
        }

        // Inputs.sizeInputs fits each field to its default, so no input on the tabs carries its own width; the boxes that
        // hold a table, the filing status choice and the grand total span both columns of a tab
        [Theory]
        [InlineData("/index.html", "/app.js", "form")]
        [InlineData("/optimal.html", "/optimal.js", "optimalForm")]
        public async Task InputTabs_FitTheirFields_InTwoColumns(string path, string script, string form)
        {
            string html = await GetAsync(path);
            string tabs = Regex.Match(html, @"<div class=""tabs-tile"">(.*?)(?=<section class=""graph-tile)", RegexOptions.Singleline).Groups[1].Value;

            Assert.NotEmpty(tabs);
            Assert.DoesNotMatch(@"<input[^>]*style=""width", tabs);
            Assert.Contains($"Inputs.sizeInputs({form});", await GetAsync(script));
            Assert.Matches(@"id=""panel-assets""[^>]*>\s*<fieldset class=""subgroup span-all"">", tabs);
            Assert.Contains(@"<fieldset class=""subgroup filing-status span-all"">", tabs);
            Assert.Contains(@"<p class=""total-line span-all"" id=""grand-total"">", tabs);
        }

        // Single / Married filing jointly sits on the Demographics tab of both pages, for every tier (nothing locks it)
        [Theory]
        [InlineData("/index.html")]
        [InlineData("/optimal.html")]
        public async Task FilingStatus_IsOnTheDemographicsTab_ForEveryTier(string path)
        {
            string html = await GetAsync(path);
            string demographics = Regex.Match(html, @"id=""panel-demographics""(.*?)(?=<div class=""tab-panel)", RegexOptions.Singleline).Groups[1].Value;
            string fieldset = Regex.Match(demographics, @"<fieldset class=""subgroup filing-status[^""]*"">(.*?)</fieldset>", RegexOptions.Singleline).Groups[1].Value;

            Assert.Contains(@"<input type=""radio"" name=""filingStatus"" value=""single"" checked>", fieldset);
            Assert.Contains(@"<input type=""radio"" name=""filingStatus"" value=""married"">", fieldset);
            Assert.Contains("data-filing-hint", fieldset);
            Assert.DoesNotContain("data-requires", fieldset);
            Assert.DoesNotContain("paid-badge", fieldset);
        }

        // Your Money: Roth spans the second column's two rows, so Brokerage sits right under Tax Deferred in the first
        [Theory]
        [InlineData("/index.html")]
        [InlineData("/optimal.html")]
        public async Task YourMoney_StacksBrokerageUnderTaxDeferred(string path)
        {
            string html = await GetAsync(path);
            string panel = Regex.Match(html, @"id=""panel-money""(.*?)(?=<div class=""tab-panel)", RegexOptions.Singleline).Groups[1].Value;
            var boxes = Regex.Matches(panel, @"<fieldset class=""([^""]+)"">\s*<legend[^>]*>(?:<span data-i18n=""([\w.]+)"">)?", RegexOptions.Singleline);

            Assert.Equal(new[] { "subgroup", "subgroup span-two-rows", "subgroup" }, boxes.Select(m => m.Groups[1].Value));
            Assert.Equal(new[] { "", "form.roth", "form.brokerage" }, boxes.Select(m => m.Groups[2].Value));
            Assert.Contains(@"data-i18n=""form.taxDeferred""", panel[..panel.IndexOf(@"data-i18n=""form.roth""", StringComparison.Ordinal)]);
        }

        // Other Income: Social Security spans the first column's two rows, so Inheritance and then the Taxes box, holding
        // only the standard deduction, stack in the second
        [Theory]
        [InlineData("/index.html")]
        [InlineData("/optimal.html")]
        public async Task OtherIncome_PutsTheStandardDeductionInATaxesBoxUnderInheritance(string path)
        {
            string html = await GetAsync(path);
            string panel = Regex.Match(html, @"id=""panel-income""(.*?)(?=<div class=""tab-panel)", RegexOptions.Singleline).Groups[1].Value;
            var boxes = Regex.Matches(panel, @"<fieldset class=""([^""]+)"">(.*?)</fieldset>", RegexOptions.Singleline);

            Assert.Equal(new[] { "subgroup span-two-rows", "subgroup", "subgroup" }, boxes.Select(m => m.Groups[1].Value));
            Assert.Contains(@"name=""newMoney""", boxes[1].Groups[2].Value);
            Assert.Contains(@"<legend data-i18n=""form.taxesLegend"">Taxes</legend>", boxes[2].Groups[2].Value);
            Assert.Equal(new[] { "annualStandardDeduction" }, Regex.Matches(boxes[2].Groups[2].Value, @"name=""(\w+)""").Select(m => m.Groups[1].Value));
        }

        // A Free visitor enters one total per account; it starts at the sum of the basis and gain fields' defaults
        [Theory]
        [InlineData("/index.html")]
        [InlineData("/optimal.html")]
        public async Task FreeAccountTotals_StartAtTheSumOfTheSplitFields(string path)
        {
            string html = await GetAsync(path);
            string panel = Regex.Match(html, @"id=""panel-money""(.*?)(?=<div class=""tab-panel)", RegexOptions.Singleline).Groups[1].Value;
            double Value(string name) => double.Parse(
                Regex.Match(panel, $@"name=""{name}""[^>]*value=""([\d,]+)""").Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture);

            Assert.Equal(Value("initialRothBasis") + Value("initialRothUnrealizedGain"), Value("rothTotal"));
            Assert.Equal(Value("initialBrokerageBasis") + Value("initialBrokerageUnrealizedGain"), Value("brokerageTotal"));
            Assert.Equal(2, Regex.Matches(panel, @"<label data-free-only=""account-basis-split"" hidden>").Count);
            Assert.Equal(4, Regex.Matches(panel, @"<label data-requires=""account-basis-split"" data-locked=""hide"">").Count);
        }

        // A paid feature's badge must stay visible once the visitor's tier has it ("✓ Plus"), so it can't sit only inside
        // Free-only text: the returns and std. devs carry one over their two columns and the correlation one beside its
        // label (the returns note just says which), and the Optimizer's Recommendation and Simulated markets headings
        // carry the full Optimizer's
        [Fact]
        public async Task EnabledBadges_HaveSpotsThatStayVisible()
        {
            string runner = await GetAsync("/index.html");
            string optimal = await GetAsync("/optimal.html");

            foreach (var html in new[] { runner, optimal })
            {
                var note = Regex.Match(html, @"<p class=""subgroup-hint""><span data-free-only=""custom-returns""(.*?)</p>").Groups[0].Value;
                Assert.Contains(@"data-requires=""custom-returns"" data-locked=""hide""", note);
                Assert.DoesNotContain("paid-badge", note);
                Assert.Matches(@"<tr class=""badge-row"">\s*<td colspan=""3""></td>\s*<td colspan=""2"" class=""ret-badge""><span class=""paid-badge"" data-feature=""custom-returns"" hidden></span></td>", html);
                Assert.Matches(@"<span data-i18n=""runner.correlation"">[^<]*</span> <span class=""paid-badge"" data-feature=""custom-returns"" hidden></span>", html);
            }
            Assert.Matches(@"<h2 id=""recommendation-title""[^>]*>.*?<span class=""paid-badge"" data-feature=""optimizer-full"" hidden></span></h2>", optimal);
            Assert.Matches(@"<legend><span data-i18n=""optimal.markets"">[^<]*</span> <span class=""paid-badge"" data-feature=""optimizer-full"" hidden></span></legend>", optimal);
            Assert.Contains("data-feature=\"tax-detail\"", await GetAsync("/app.js"));
            // The annuity's badge beside "Include an annuity", the COLA's beside its checkbox and its one rate in small print
            Assert.Matches(@"name=""includeAnnuity""[^>]*>\s*<span data-i18n=""runner.annuity.include"">[^<]*</span><sup class=""footnote"">.*?</sup>\s*<span class=""paid-badge"" data-feature=""annuity"" hidden></span>", runner);
            Assert.Matches(@"name=""annuityCola""[^>]*>\s*<span data-i18n=""runner.annuity.cola"">[^<]*</span>\s*<small class=""cola-rate"" data-i18n=""runner.annuity.colaRateNote"">2% a year</small>\s*<span class=""paid-badge"" data-feature=""annuity-cola"" hidden></span>", runner);
            Assert.Matches(@"<legend><span data-i18n=""runner.annuity.gender"">[^<]*</span> <span class=""paid-badge"" data-feature=""annuity-gender"" hidden></span></legend>", runner);
        }

        // The Annuity tab: Include (off), then the payment ($2,600) and investment ($400,000), SPIA or DIA (SPIA chosen;
        // the DIA's date hidden until chosen, its minimum set by app.js), tax-deferred, and the COLA (its one rate, 2%).
        // Its fields sit in a fieldset that stays disabled until Include is ticked; the * and 1-3 are footnotes like the year
        // tables': a superscript link whose tooltip holds the note.
        [Fact]
        public async Task ScenarioRunner_AnnuityTab_HasItsDefaultsAndNotes()
        {
            string html = await GetAsync("/index.html");
            string panel = Regex.Match(html, @"id=""panel-annuity""(.*?)(?=<section class=""graph-tile)", RegexOptions.Singleline).Groups[1].Value;

            Assert.Matches(@"<input type=""checkbox"" name=""includeAnnuity""(?![^>]*checked)", panel);
            Assert.Contains(@"<fieldset class=""annuity-details"" id=""annuity-details"" disabled>", panel);
            Assert.Matches(@"name=""annuityMonthlyAmount""[^>]*value=""2,600""", panel);
            Assert.Matches(@"name=""annuityInvestment""[^>]*value=""400,000""", panel);
            Assert.Contains(@"name=""annuityType"" value=""spia"" checked", panel);
            Assert.Matches(@"name=""annuityType"" value=""dia""(?![^>]*checked)", panel);
            Assert.Contains(@"<label id=""annuity-start"" hidden>", panel);
            Assert.Matches(@"<input type=""date"" name=""annuityStartDate""[^>]*disabled", panel);
            // No rate to choose: the app has one COLA
            Assert.DoesNotContain("annuityColaRate", panel);
            Assert.DoesNotContain("<select", panel);
            foreach (var marker in new[] { "*", "1", "2", "3" })
                Assert.Matches($@"<sup class=""footnote""><a href=""#"" class=""footnote-ref"" aria-label=""[^""]+"" data-i18n-aria-label=""runner.annuity.note\w+Aria"">{Regex.Escape(marker)}</a><span class=""footnote-tip"" role=""tooltip"" data-i18n=""runner.annuity.note\w+"">", panel);
            Assert.DoesNotContain("form-notes", panel);

            // The gender (Pro) only sets the suggested SPIA rate, whose block app.js fills: hidden until there is one. The
            // Gender box itself is hidden to keep the tab short, so the rate is a man's
            Assert.Contains(@"<fieldset class=""subgroup"" id=""annuity-gender"" hidden>", panel);
            Assert.Contains(@"name=""annuitySex"" value=""male"" checked", panel);
            Assert.Matches(@"name=""annuitySex"" value=""female""(?![^>]*checked)", panel);
            Assert.Contains(@"<div class=""annuity-rate span-all"" id=""annuity-rate"" aria-live=""polite"" hidden>", panel);
            Assert.Contains(@"id=""annuity-rate-use""", panel);
        }

        [Fact]
        public async Task PaidBadges_OpenTheAccessCodePageInANewTab()
        {
            string script = await GetAsync("/paywall.js");

            // Both offer links (a badge's flyout, Upgrade to Pro) are subscribeHref, carrying this page as return=
            Assert.Contains("`/billing/subscribe?tier=${encodeURIComponent(tier.toLowerCase())}&return=${returnParam()}`", script);
            Assert.Equal(2, Regex.Matches(script, Regex.Escape("<a href=\"${subscribeHref(")).Count);
            Assert.DoesNotContain("href=\"/billing/subscribe", script);
            Assert.Contains("target=\"_blank\" rel=\"noopener\"", script);
            // The header's tier marker goes to the access page the same way
            Assert.Contains("marker.href = `/access.html?return=${returnParam()}`;", script);
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

        // The results summary's second line (average ending balance and return, each with its std. dev.), shown folded or
        // not, and a failed run's year table ending with what its last year still paid - or that the money is gone
        [Fact]
        public async Task ScenarioRunner_Results_SummarizeTheOutcome_AndSayWhatAFailedRunStillPays()
        {
            string script = await GetAsync("/app.js");
            string en = await GetAsync("/i18n/en.json");

            Assert.Contains(@"${headline}<span class=""summary-outcome"">${outcome}</span></summary>", script);
            Assert.Contains("t('runner.outcome', {", script);
            Assert.Contains("renderRunDetailTable(run.years, view.parameters.enableRothConversions, view.taxDetail, run.failed)", script);
            Assert.Contains(@"""runner.stillReceive.none"": ""No more income or balances remain: completely out of money.""", en);
            Assert.DoesNotContain("runner.realized", script);
        }

        // The app's one COLA: the page's numbers are Core's (2% each January, a first year 10% below a level payment)
        [Fact]
        public async Task ScenarioRunner_PricesTheCola_AsCoreDoes()
        {
            string script = await GetAsync("/app.js");

            Assert.Contains($"const COLA_RATE = {MonteCarloSimulation.Core.Annuity.ColaIncrease.ToString(CultureInfo.InvariantCulture)};", script);
            Assert.Contains($"const COLA_FACTOR = {MonteCarloSimulation.Core.Annuity.ColaFirstYearFactor.ToString(CultureInfo.InvariantCulture)};", script);
        }

        // Optimize beside the Monthly Withdrawal, before Run: a plain button (not a submit) that streams the Optimizer's
        // answer for these inputs from /api/run/optimize, read like the Optimal page's stream
        [Fact]
        public async Task ScenarioRunner_OffersOptimize_BesideTheWithdrawal()
        {
            string html = await GetAsync("/index.html");
            string script = await GetAsync("/app.js");

            Assert.Matches(@"</span>\s*<!--[^>]*-->\s*<button type=""button"" id=""optimize-run"" class=""secondary-button"" data-i18n=""runner.optimize""[^>]*>Optimize</button>\s*<button type=""submit"" data-i18n=""runner.submit"">Run</button>", html);
            Assert.Contains("fetch('/api/run/optimize'", script);
            Assert.Contains("await Inputs.readEvents(response", script);
            Assert.Contains("await Inputs.readEvents(response", await GetAsync("/optimal.js"));
            Assert.DoesNotContain("async function readEvents", await GetAsync("/optimal.js"));
        }

        // Optimize Social Security (Other Income, Plus): a button with the Plus badge under the Social Security fields,
        // opening a flyout - its own form, outside the scenario's - with the Optimal page's 62/67/70 estimates
        [Fact]
        public async Task ScenarioRunner_OffersOptimizeSocialSecurity_InOtherIncome()
        {
            string html = await GetAsync("/index.html");
            string script = await GetAsync("/app.js");
            string income = Regex.Match(html, @"id=""panel-income""(.*?)(?=<div class=""tab-panel)", RegexOptions.Singleline).Groups[1].Value;

            Assert.Matches(@"<button type=""button"" id=""optimize-ss""[^>]*data-i18n=""runner.optimizeSs"">Optimize Social Security</button>\s*<span class=""paid-badge"" data-feature=""optimizer-full"" hidden></span>", income);
            string runForm = Regex.Match(html, @"<form id=""run-form"">(.*?)</form>", RegexOptions.Singleline).Groups[1].Value;
            Assert.DoesNotContain("ss-flyout", runForm.Replace(@"aria-controls=""ss-flyout""", ""));
            string flyout = Regex.Match(html, @"<form id=""ss-flyout""(.*?)</form>", RegexOptions.Singleline).Groups[1].Value;
            Assert.All(new[] { "ssAt62", "ssAt67", "ssAt70" }, name => Assert.Contains($@"name=""{name}""", flyout));
            Assert.Contains(@"data-i18n=""optimal.ssLegend""", flyout);
            Assert.Contains("fetch('/api/run/optimize-social-security'", script);
        }

        [Fact]
        public async Task ScenarioRunner_SharesAScenarioAsALink()
        {
            string html = await GetAsync("/index.html");
            string script = await GetAsync("/app.js");

            // Share scenario beside Run (hidden until there are results) and in the results summary, both copying the link
            Assert.Matches(@"<span id=""run-status""[^>]*></span>\s*<button type=""button"" id=""share-scenario"" class=""secondary-button share-scenario""[^>]*hidden>", html);
            Assert.Contains(@"<button type=""button"" class=""secondary-button share-scenario"">${t('runner.share.button')}</button>", script);
            Assert.Contains("navigator.clipboard.writeText(url)", script);
            // The link's format lives in scenario-link.js, loaded before the page script
            int link = html.IndexOf(@"<script src=""scenario-link.js"">");
            Assert.InRange(link, 1, html.IndexOf(@"<script src=""app.js"">"));
            Assert.Contains("ScenarioLink.encode(view.request)", script);
            // Opened once the locks are on, so a Free visitor's locked inputs keep their Free values
            Assert.Contains("Paywall.ready.then(applySharedScenario);", script);
            Assert.Contains("&shared=1&return=", script);
        }

        // The survival headline's face: a frown below 60%, a straight face from 60% to under 75%, a smile from 75%, and
        // a smile when every run survived
        [Fact]
        public async Task ScenarioRunner_Results_ShowAFaceForTheSurvivalRate()
        {
            string app = await GetAsync("/app.js");

            Assert.Matches(@"function survivalFace\(survival\) \{\s*if \(survival < 0\.6\) return '🙁';\s*if \(survival < 0\.75\) return '😐';\s*return '🙂';", app);
            Assert.Contains("${survivalFace(survival)} ${t('runner.failedCount'", app);
            Assert.Contains("${survivalFace(1)} ${t('runner.allSurvived')}", app);
        }

        // Gordon, the guided setup: its scripted conversation and then its panel load after the page script, it shows only
        // while /api/me says the Observe switch is on, and it hears about each finished run from app.js
        [Fact]
        public async Task ScenarioRunner_LoadsGordon_AfterThePageScript_BehindItsSwitch()
        {
            string html = await GetAsync("/index.html");
            string app = await GetAsync("/app.js");
            string gordon = await GetAsync("/gordon.js");
            string observe = await GetAsync("/observe.js");

            int page = html.IndexOf(@"<script src=""app.js""></script>");
            int conversation = html.IndexOf(@"<script src=""gordon-script.js""></script>");
            int panel = html.IndexOf(@"<script src=""gordon.js""></script>");
            Assert.True(page > 0 && conversation > page && panel > conversation);
            Assert.Contains("Paywall.me.gordon", gordon);
            Assert.Contains("new CustomEvent('runner:result', { detail: view })", app);
            Assert.Contains("'runner:result'", gordon);
            // Gordon can press Optimize: app.js reports its progress and what it put into the form, and a stopped
            // Optimize still sends runner:result, so Gordon never waits on a run that won't come
            Assert.Contains("document.getElementById('optimize-run').click()", gordon);
            Assert.Contains("new CustomEvent('runner:optimizing'", app);
            Assert.Contains("new CustomEvent('runner:optimized', { detail: { ...handoff } })", app);
            Assert.Equal(2, Regex.Matches(app, @"new CustomEvent\('runner:result', \{ detail: view \}\)").Count);
            Assert.Contains("'runner:optimized'", gordon);
            Assert.Contains(@"data-tier=""gordon""", observe);
            Assert.Contains("Enable Gordon", observe);
        }

        [Fact]
        public async Task AccessPage_FromASharedLink_OffersToContinueWithoutACode_AndGoesBackInThisTab()
        {
            string html = await GetAsync("/access.html");
            string script = await GetAsync("/access.js");

            Assert.Contains(@"<div id=""access-shared"" class=""handoff-note access-shared"" hidden>", html);
            Assert.True(HasId(html, "access-shared-continue"));
            Assert.Contains("get('shared') === '1'", script);
            // This tab holds the visitor's scenario, so going back navigates it rather than closing it
            Assert.Matches(@"if \(shared\) \{\s*location\.replace\(returnTo\);\s*return;\s*\}\s*window\.close\(\);", script);
        }
    }
}
