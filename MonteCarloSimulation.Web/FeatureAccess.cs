using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

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

    // What one visitor may use. Gated is false while the Subscriptions flag is off: every feature is then available and
    // nothing is offered, exactly as before paid tiers. GET /api/me returns this as is.
    public sealed record Access(bool Gated, Tier Tier, IReadOnlyList<string> Features, IReadOnlyList<Offer> Offers)
    {
        public bool Can(string feature) => !Gated || Features.Contains(feature);
    }

    // The one place that decides access: the release flags, then the visitor's tier and that tier's configured features.
    // A tier whose flag is off isn't offered and its code doesn't count; with Plus off, its features are free to everyone,
    // since there's nothing to buy.
    public sealed class FeatureAccess(IFeatureManager flags, IEntitlements entitlements, IOptionsMonitor<TiersOptions> tiers)
    {
        public async Task<Access> ForAsync(HttpContext context)
        {
            if (!await flags.IsEnabledAsync(Flags.Subscriptions))
                return new Access(false, Tier.Free, Features.All, []);

            var settings = tiers.CurrentValue;
            bool plusOffered = await flags.IsEnabledAsync(Flags.TierPlus);
            bool proOffered = await flags.IsEnabledAsync(Flags.TierPro);

            var tier = entitlements.TierOf(context);
            if ((tier == Tier.Plus && !plusOffered) || (tier == Tier.Pro && !proOffered)) tier = Tier.Free;

            var granted = new HashSet<string>(settings.For(tier)?.Features ?? []);
            if (!plusOffered) granted.UnionWith(settings.Plus.Features);

            var offers = new List<Offer>();
            if (plusOffered) offers.Add(new Offer(Tier.Plus, settings.Plus.PriceLabel, "code", settings.Plus.Features));
            if (proOffered) offers.Add(new Offer(Tier.Pro, settings.Pro.PriceLabel, "code", settings.Pro.Features));

            return new Access(true, tier, Features.All.Where(granted.Contains).ToList(), offers);
        }

        public async Task<bool> IsOfferedAsync(Tier tier) =>
            tier != Tier.Free && await flags.IsEnabledAsync(Flags.Subscriptions) && await flags.IsEnabledAsync(Flags.For(tier));
    }
}
