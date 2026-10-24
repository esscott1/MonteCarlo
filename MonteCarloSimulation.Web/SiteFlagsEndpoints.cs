using Microsoft.Extensions.Options;

namespace MonteCarloSimulation.Web
{
    // The Observe page's Features section: the site's mode, the Plus and Pro switches and the Gordon switch (GET), and
    // flipping one (PUT /api/features/plus, /pro or /gordon). Both need the Observe token in the X-Observe-Token header,
    // the one the change-request passphrase issues; without it they're a 401. Sent in a header rather than a cookie,
    // another site can't flip a switch.
    public static class SiteFlagsEndpoints
    {
        public const string GordonSwitch = "gordon";

        public static void MapSiteFlags(this WebApplication app)
        {
            app.MapGet("/api/features", async (HttpContext context, IConfiguration config, SiteFlags flags, IOptionsMonitor<TiersOptions> tiers) =>
                ObserveAccessToken.Allows(context.Request, config)
                    ? Results.Ok(await DescribeAsync(flags, tiers.CurrentValue))
                    : Results.StatusCode(StatusCodes.Status401Unauthorized));

            app.MapPut("/api/features/{name}", async (
                string name,
                SiteFlagUpdate update,
                HttpContext context,
                IConfiguration config,
                SiteFlags flags,
                IOptionsMonitor<TiersOptions> tiers,
                ILogger<SiteFlags> logger) =>
            {
                if (!ObserveAccessToken.Allows(context.Request, config))
                    return Results.StatusCode(StatusCodes.Status401Unauthorized);

                if (string.Equals(name, GordonSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    flags.Set(Flags.Gordon, update.On);
                    logger.LogWarning("Observe: Gordon switched {State} for the whole site by {Address}.",
                        update.On ? "on" : "off", context.Connection.RemoteIpAddress);
                    return Results.Ok(await DescribeAsync(flags, tiers.CurrentValue));
                }

                if (!TierNames.TryParse(name, out var paid) || paid == Tier.Free)
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
            var gordon = await flags.StateOfAsync(Flags.Gordon);
            return new SiteFlagsView(
                SiteModes.For(On(Tier.Plus), On(Tier.Pro)),
                states.Select(s => new SiteFlagView(s.Tier, s.On, s.ChangedAt, tiers.For(s.Tier)!.PriceLabel, tiers.For(s.Tier)!.Features)).ToList(),
                new SiteSwitchView(gordon.On, gordon.ChangedAt));
        }
    }

    public sealed record SiteFlagUpdate(bool On);

    public sealed record SiteFlagView(Tier Tier, bool On, DateTimeOffset? ChangedAt, string PriceLabel, IReadOnlyList<string> Features);

    public sealed record SiteSwitchView(bool On, DateTimeOffset? ChangedAt);

    public sealed record SiteFlagsView(string Mode, IReadOnlyList<SiteFlagView> Tiers, SiteSwitchView Gordon);
}
