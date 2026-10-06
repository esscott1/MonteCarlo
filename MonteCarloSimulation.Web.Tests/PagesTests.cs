using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MonteCarloSimulation.Web.Tests
{
    // The site's page layout: "/" lands on the Optimal page, titled Monte Carlo Portfolio Optimizer, whose hamburger menu
    // leads to the Scenario runner (index.html), Model Info and Observe. The Scenario runner keeps the change-request
    // pencil and has a back arrow home; the other sub-pages go back to "/".
    public class PagesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
    {
        private Task<string> GetAsync(string path) => factory.CreateClient().GetStringAsync(path);

        private static string Title(string html) => Regex.Match(html, "<title[^>]*>(.*?)</title>").Groups[1].Value;
        private static string Heading(string html) => Regex.Match(html, "<h1[^>]*>(.*?)</h1>").Groups[1].Value;
        private static bool HasId(string html, string id) => html.Contains($"id=\"{id}\"");

        [Fact]
        public async Task Root_IsTheOptimalPage_WithTheMenu()
        {
            string html = await GetAsync("/");

            Assert.Equal("Monte Carlo Portfolio Optimizer", Title(html));
            Assert.Equal("Monte Carlo Portfolio Optimizer", Heading(html));
            Assert.True(HasId(html, "optimal-form"));

            // The menu, in order: Scenario runner, Model Info, Translations, Observe
            Assert.True(HasId(html, "hamburger-toggle"));
            Assert.True(HasId(html, "observe-flyout"));
            var menu = Regex.Match(html, @"<div id=""hamburger-menu""[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
            var items = Regex.Matches(menu, @"role=""menuitem""[^>]*>([^<]+)<").Select(m => m.Groups[1].Value);
            Assert.Equal(new[] { "Scenario runner", "Model Info", "Translations", "Observe" }, items);
            Assert.Matches(@"<a href=""index.html"" id=""scenario-runner-menu-item"" role=""menuitem""[^>]*>Scenario runner</a>", menu);

            // The pencil stays on the Scenario runner; this page is home, so no back arrow
            Assert.False(HasId(html, "edit-toggle"));
            Assert.DoesNotContain(@"aria-label=""Back to home""", html);

            // menu.js loads before optimal.js
            int menuScript = html.IndexOf("<script src=\"menu.js\"></script>");
            int optimalScript = html.IndexOf("<script src=\"optimal.js\"></script>");
            Assert.True(menuScript >= 0 && optimalScript > menuScript);
        }

        [Fact]
        public async Task Root_AndOptimalHtml_ServeTheSamePage()
        {
            Assert.Equal(await GetAsync("/optimal.html"), await GetAsync("/"));
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
            Assert.Equal(new[] { "tab-demographics", "tab-money", "tab-income", "tab-investments" }, tabs.Select(m => m.Groups[1].Value));
            Assert.Equal(new[] { "true", "false", "false", "false" }, tabs.Select(m => m.Groups[3].Value));
            foreach (Match tab in tabs)
                Assert.True(HasId(html, tab.Groups[2].Value));

            // Each input sits on its tab
            string Panel(string id) => Regex.Match(html, $@"id=""{id}""(.*?)(?=<div class=""tab-panel|<section class=""graph-tile)", RegexOptions.Singleline).Groups[1].Value;
            Assert.All(new[] { "retirementDate", "birthdate", "years", "iterations" }, name => Assert.Contains($@"name=""{name}""", Panel("panel-demographics")));
            Assert.All(new[] { "withdrawal", "initialTaxableBalance", "initialRothBasis", "initialRothUnrealizedGain", "initialBrokerageBasis", "initialBrokerageUnrealizedGain" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-money")));
            Assert.All(new[] { "socialSecurityMonthlyAmount", "socialSecurityStartDate", "annualStandardDeduction", "newMoney", "yearNewMoney", "enableRothConversions" },
                name => Assert.Contains($@"name=""{name}""", Panel("panel-income")));

            // The investments default to 60/30/10, 0.1 correlation, and 8/19, 4.5/4, 3.5/1
            string investments = Panel("panel-investments");
            var defaults = new Dictionary<string, string>
            {
                ["stockAllocation"] = "60", ["bondAllocation"] = "30", ["cashAllocation"] = "10", ["stockBondCorrelation"] = "0.1",
                ["stockReturn"] = "8", ["stockStdDev"] = "19", ["bondReturn"] = "4.5", ["bondStdDev"] = "4", ["cashReturn"] = "3.5", ["cashStdDev"] = "1",
            };
            foreach (var (name, value) in defaults)
                Assert.Matches($@"name=""{name}""[^>]*value=""{Regex.Escape(value)}""", investments);

            // The chart placeholder is the inputs' neighbour, and the preset radios are gone
            Assert.Contains(@"<section class=""graph-tile""", html);
            Assert.False(HasId(html, "scenario-options"));
        }

        [Theory]
        [InlineData("/model-info.html")]
        [InlineData("/observe.html")]
        [InlineData("/translations.html")]
        public async Task SubPages_GoBackToTheLandingPage(string path)
        {
            string html = await GetAsync(path);

            Assert.Matches(@"<a href=""/"" class=""icon-button"" aria-label=""Back to home""[^>]*>", html);
            Assert.DoesNotContain("href=\"index.html\"", html);
        }

        // English/Spanish: each translated page has the language button and loads i18n.js before its own script.
        // The Observe page stays English.
        [Theory]
        [InlineData("/", "optimal.js")]
        [InlineData("/index.html", "app.js")]
        [InlineData("/model-info.html", "model-info.js")]
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
