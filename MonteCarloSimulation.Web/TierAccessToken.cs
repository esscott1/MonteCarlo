using System.Security.Cryptography;
using System.Text;

namespace MonteCarloSimulation.Web
{
    // The signed cookie a correct access code earns: "Plus.<expiry>.<signature>", where the signature is an HMAC of the
    // tier and expiry keyed by that tier's access code. Like ObserveAccessToken it's stateless, so it survives restarts and
    // works on any instance; changing a tier's code invalidates every token issued with the old one, and editing the tier
    // in the cookie breaks the signature. Temporary: Entra sign-in and Stripe replace it.
    public static class TierAccessToken
    {
        public const string CookieName = "tier-access";
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

        public static string Issue(Tier tier, string accessCode, DateTimeOffset now)
        {
            long expiry = now.Add(Lifetime).ToUnixTimeSeconds();
            return $"{tier}.{expiry}.{Convert.ToBase64String(ComputeSignature(tier, expiry, accessCode))}";
        }

        // The tier a token grants, or Free for a missing, malformed, expired or forged one. codeFor gives each tier's
        // current access code (null when it has none).
        public static Tier Read(string? token, Func<Tier, string?> codeFor, DateTimeOffset now)
        {
            if (string.IsNullOrEmpty(token)) return Tier.Free;

            var parts = token.Split('.', 3);
            if (parts.Length != 3) return Tier.Free;
            if (!Enum.TryParse<Tier>(parts[0], ignoreCase: false, out var tier) || tier == Tier.Free || parts[0] != tier.ToString())
                return Tier.Free;
            if (!long.TryParse(parts[1], out var expiry) || now.ToUnixTimeSeconds() > expiry) return Tier.Free;

            var code = codeFor(tier);
            if (string.IsNullOrEmpty(code)) return Tier.Free;

            byte[] supplied;
            try
            {
                supplied = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return Tier.Free;
            }

            return CryptographicOperations.FixedTimeEquals(supplied, ComputeSignature(tier, expiry, code)) ? tier : Tier.Free;
        }

        private static byte[] ComputeSignature(Tier tier, long expiry, string accessCode) =>
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(accessCode), Encoding.UTF8.GetBytes($"{tier}.{expiry}"));
    }
}
