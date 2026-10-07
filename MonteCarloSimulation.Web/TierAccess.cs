using Microsoft.Extensions.Options;

namespace MonteCarloSimulation.Web
{
    // The access-code page's request (access.html): a paid tier and its code.
    public sealed class TierAccessRequest
    {
        public string Tier { get; set; } = "";
        public string Code { get; set; } = "";

        // Checks run cheapest-first: the tier is one on offer with a code configured, then the code itself, compared in
        // fixed time. On success, tier is the one to grant.
        public Dictionary<string, string> Check(bool offered, TierSettings? settings, out Tier tier)
        {
            var errors = new Dictionary<string, string>();
            TierNames.TryParse(Tier, out tier);
            if (!offered || settings is null || string.IsNullOrEmpty(settings.AccessCode))
                errors["tier"] = "That tier isn't available.";
            else if (string.IsNullOrWhiteSpace(Code))
                errors["code"] = "Enter the access code.";
            else if (!ChangeRequest.PassphraseMatches(Code.Trim(), settings.AccessCode))
                errors["code"] = "Incorrect access code.";
            return errors;
        }
    }

    // The temporary stand-in for sign-in and checkout: what a visitor may use (/api/me), entering a tier's access code
    // (/api/tier-access, which sets the signed cookie) or going back to Free (DELETE), and /billing/subscribe - the one
    // link the paid-feature badges use, which goes to the access-code page now and to Stripe Checkout later.
    public static class TierEndpoints
    {
        public static void MapTierAccess(this WebApplication app)
        {
            // What this visitor may use, and how a Free visitor's account totals are split (the pages split them the same way)
            app.MapGet("/api/me", async (HttpContext context, FeatureAccess access, IOptionsMonitor<FreeDefaultsOptions> freeDefaults) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var me = await access.ForAsync(context);
                var free = freeDefaults.CurrentValue;
                return Results.Ok(new
                {
                    me.Gated,
                    me.Tier,
                    me.Features,
                    me.Offers,
                    FreeDefaults = new { free.BrokerageGainShare, free.RothBasisShare }
                });
            });

            app.MapPost("/api/tier-access", async (
                TierAccessRequest request,
                HttpContext context,
                FeatureAccess access,
                IOptionsMonitor<TiersOptions> tiers,
                TimeProvider clock,
                ILogger<TierAccessRequest> logger) =>
            {
                bool parsed = TierNames.TryParse(request.Tier, out var requested);
                bool offered = parsed && await access.IsOfferedAsync(requested);
                var errors = request.Check(offered, offered ? tiers.CurrentValue.For(requested) : null, out var tier);
                if (errors.Count > 0)
                {
                    if (errors.ContainsKey("code")) logger.LogWarning("Access code rejected for {Tier}.", tier);
                    return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
                }

                var now = clock.GetUtcNow();
                context.Response.Cookies.Append(
                    TierAccessToken.CookieName,
                    TierAccessToken.Issue(tier, tiers.CurrentValue.For(tier)!.AccessCode!, now),
                    CookieOptions(now.Add(TierAccessToken.Lifetime)));
                logger.LogInformation("Access code accepted for {Tier}.", tier);
                return Results.Ok(new { tier });
            }).AddEndpointFilter(new QuotaFilter(
                app.Services.GetRequiredKeyedService<RequestQuota>(Quotas.TierAccess), "Too many access code attempts from this address."));

            app.MapDelete("/api/tier-access", (HttpContext context) =>
            {
                context.Response.Cookies.Delete(TierAccessToken.CookieName, CookieOptions(null));
                return Results.NoContent();
            });

            app.MapGet("/billing/subscribe", (string? tier) =>
                Results.Redirect(TierNames.TryParse(tier, out var t) && t != Tier.Free
                    ? $"/access.html?tier={t.ToString().ToLowerInvariant()}"
                    : "/access.html"));
        }

        private static CookieOptions CookieOptions(DateTimeOffset? expires) => new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            Expires = expires
        };
    }
}
