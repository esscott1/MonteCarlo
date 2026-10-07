using Microsoft.Extensions.Options;

namespace MonteCarloSimulation.Web
{
    // Which tier a visitor has. Phase 1 reads the access-code cookie (AccessCodeEntitlements); Stripe-backed entitlements
    // replace it later without anything else changing.
    public interface IEntitlements
    {
        Tier TierOf(HttpContext context);
    }

    public sealed class AccessCodeEntitlements(IOptionsMonitor<TiersOptions> tiers, TimeProvider clock) : IEntitlements
    {
        public Tier TierOf(HttpContext context) => TierAccessToken.Read(
            context.Request.Cookies[TierAccessToken.CookieName],
            tier => tiers.CurrentValue.For(tier)?.AccessCode,
            clock.GetUtcNow());
    }

    // A tier the pages can offer: its price, how to get it ("code" while access codes stand in for checkout) and the
    // features it includes, so a paid-feature badge can name the cheapest tier that unlocks it.
    public sealed record Offer(Tier Tier, string PriceLabel, string Action, IReadOnlyList<string> Features);

    // What one visitor may use: the site's mode (SiteModes), their tier and its features, and the tiers on offer.
    // GET /api/me returns this as is.
    public sealed record Access(string Mode, Tier Tier, IReadOnlyList<string> Features, IReadOnlyList<Offer> Offers)
    {
        public bool Can(string feature) => Features.Contains(feature);
    }

    // The one place that decides access: the site's switches (SiteFlags), then the visitor's tier and that tier's
    // configured features. A tier whose switch is off isn't offered and its code doesn't count, so with both off (Free
    // Only) everyone has the Free version and nothing to upgrade to.
    public sealed class FeatureAccess(SiteFlags flags, IEntitlements entitlements, IOptionsMonitor<TiersOptions> tiers)
    {
        public async Task<Access> ForAsync(HttpContext context)
        {
            var settings = tiers.CurrentValue;
            bool plusOffered = await flags.IsOnAsync(Tier.Plus);
            bool proOffered = await flags.IsOnAsync(Tier.Pro);

            var tier = entitlements.TierOf(context);
            if ((tier == Tier.Plus && !plusOffered) || (tier == Tier.Pro && !proOffered)) tier = Tier.Free;

            var granted = new HashSet<string>(settings.For(tier)?.Features ?? []);

            var offers = new List<Offer>();
            if (plusOffered) offers.Add(new Offer(Tier.Plus, settings.Plus.PriceLabel, "code", settings.Plus.Features));
            if (proOffered) offers.Add(new Offer(Tier.Pro, settings.Pro.PriceLabel, "code", settings.Pro.Features));

            return new Access(SiteModes.For(plusOffered, proOffered), tier, Features.All.Where(granted.Contains).ToList(), offers);
        }

        public Task<bool> IsOfferedAsync(Tier tier) => flags.IsOnAsync(tier);
    }
}
