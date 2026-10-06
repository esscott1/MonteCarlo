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

        private static string Title(string html) => Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value;
        private static string Heading(string html) => Regex.Match(html, "<h1>(.*?)</h1>").Groups[1].Value;
        private static bool HasId(string html, string id) => html.Contains($"id=\"{id}\"");

        [Fact]
        public async Task Root_IsTheOptimalPage_WithTheMenu()
        {
            string html = await GetAsync("/");

            Assert.Equal("Monte Carlo Portfolio Optimizer", Title(html));
            Assert.Equal("Monte Carlo Portfolio Optimizer", Heading(html));
            Assert.True(HasId(html, "optimal-form"));

            // The menu, in order: Scenario runner, Model Info, Observe
            Assert.True(HasId(html, "hamburger-toggle"));
            Assert.True(HasId(html, "observe-flyout"));
            var menu = Regex.Match(html, @"<div id=""hamburger-menu""[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
            var items = Regex.Matches(menu, @"role=""menuitem"">([^<]+)<").Select(m => m.Groups[1].Value);
            Assert.Equal(new[] { "Scenario runner", "Model Info", "Observe" }, items);
            Assert.Contains(@"<a href=""index.html"" id=""scenario-runner-menu-item"" role=""menuitem"">Scenario runner</a>", menu);

            // The pencil stays on the Scenario runner; this page is home, so no back arrow
            Assert.False(HasId(html, "edit-toggle"));
            Assert.DoesNotContain("Back to home", html);

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
            Assert.Contains(@"<a href=""/"" class=""icon-button"" aria-label=""Back to home"">", html);

            // The menu moved to the landing page
            Assert.False(HasId(html, "hamburger-toggle"));
            Assert.False(HasId(html, "observe-flyout"));
        }

        [Theory]
        [InlineData("/model-info.html")]
        [InlineData("/observe.html")]
        public async Task SubPages_GoBackToTheLandingPage(string path)
        {
            string html = await GetAsync(path);

            Assert.Contains(@"<a href=""/"" class=""icon-button"" aria-label=""Back to home"">", html);
            Assert.DoesNotContain("href=\"index.html\"", html);
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

            Assert.Contains(">Run in Scenario runner</button>", script);
            Assert.Contains("window.location.href = 'index.html';", script);
            Assert.DoesNotContain("Run in Simulator", script);
        }
    }
}
