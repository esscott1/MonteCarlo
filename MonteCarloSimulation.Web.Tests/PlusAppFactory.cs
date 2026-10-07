using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MonteCarloSimulation.Web.Tests
{
    // The web app as a Plus visitor sees it - every input editable, the full results - for the endpoint tests that pin
    // the app's own behaviour. The site's switches are kept in a fresh temporary file, so a developer's own Observe
    // switches (App_Data) don't leak in. TierAccessTests and SiteFlagsTests cover the tiers themselves.
    public sealed class PlusAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("SiteFlags:Path", TempSiteFlags.NewPath());
            builder.ConfigureTestServices(services => services.AddSingleton<IEntitlements>(new FixedEntitlements(Tier.Plus)));
        }
    }

    public sealed class FixedEntitlements(Tier tier) : IEntitlements
    {
        public Tier TierOf(HttpContext context) => tier;
    }

    public static class TempSiteFlags
    {
        // A path no test has used: no file yet, so every switch starts at its default
        public static string NewPath() => Path.Combine(Path.GetTempPath(), "montecarlo-tests", $"site-flags-{Guid.NewGuid():N}.json");
    }
}
