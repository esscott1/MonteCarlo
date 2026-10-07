using Microsoft.Extensions.Options;

namespace MonteCarloSimulation.Web
{
    // The Observe page's Features section: the site's mode and the Plus and Pro switches (GET), and flipping one (PUT).
    // Both need the Observe token in the X-Observe-Token header, the one the change-request passphrase issues; without
    // it they're a 401. Sent in a header rather than a cookie, another site can't flip a switch.
    public static class SiteFlagsEndpoints
    {
        public static void MapSiteFlags(this WebApplication app)
        {
            app.MapGet("/api/features", async (HttpContext context, IConfiguration config, SiteFlags flags, IOptionsMonitor<TiersOptions> tiers) =>
                ObserveAccessToken.Allows(context.Request, config)
                    ? Results.Ok(await DescribeAsync(flags, tiers.CurrentValue))
                    : Results.StatusCode(StatusCodes.Status401Unauthorized));

            app.MapPut("/api/features/{tier}", async (
                string tier,
                SiteFlagUpdate update,
                HttpContext context,
                IConfiguration config,
                SiteFlags flags,
                IOptionsMonitor<TiersOptions> tiers,
                ILogger<SiteFlags> logger) =>
            {
                if (!ObserveAccessToken.Allows(context.Request, config))
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);
                if (!TierNames.TryParse(tier, out var paid) || paid == Tier.Free)
                    return Results.NotFound();

                flags.Set(paid, update.On);
                logger.LogWarning("Observe: {Tier} switched {State} for the whole site by {Address}.",
                    paid, update.On ? "on" : "off", context.Connection.RemoteIpAddress);
                return Results.Ok(await DescribeAsync(flags, tiers.CurrentValue));
            });
        }

        private static async Task<SiteFlagsView> DescribeAsync(SiteFlags flags, TiersOptions tiers)
        {
            var states = await flags.ListAsync();
            bool On(Tier tier) => states.Single(s => s.Tier == tier).On;
            return new SiteFlagsView(
                SiteModes.For(On(Tier.Plus), On(Tier.Pro)),
                states.Select(s => new SiteFlagView(s.Tier, s.On, s.ChangedAt, tiers.For(s.Tier)!.PriceLabel, tiers.For(s.Tier)!.Features)).ToList());
        }
    }

    public sealed record SiteFlagUpdate(bool On);

    public sealed record SiteFlagView(Tier Tier, bool On, DateTimeOffset? ChangedAt, string PriceLabel, IReadOnlyList<string> Features);

    public sealed record SiteFlagsView(string Mode, IReadOnlyList<SiteFlagView> Tiers);
}
