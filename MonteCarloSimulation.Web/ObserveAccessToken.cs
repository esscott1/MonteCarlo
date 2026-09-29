using System.Security.Cryptography;
using System.Text;

namespace MonteCarloSimulation.Web
{
    // A stateless, signed session token gating access to the Observe dashboard. Verification
    // is pure computation from the token plus the shared passphrase secret - no server-side
    // session store - so it works unchanged whether the app is running as one instance or
    // scaled out to several.
    public static class ObserveAccessToken
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

        public static string Issue(string passphraseSecret)
        {
            long expiry = DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds();
            return $"{expiry}.{Convert.ToBase64String(ComputeSignature(expiry, passphraseSecret))}";
        }

        public static bool IsValid(string? token, string passphraseSecret)
        {
            if (string.IsNullOrEmpty(token)) return false;

            var parts = token.Split('.', 2);
            if (parts.Length != 2) return false;
            if (!long.TryParse(parts[0], out var expiry)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry) return false;

            byte[] suppliedSignature;
            try
            {
                suppliedSignature = Convert.FromBase64String(parts[1]);
            }
            catch (FormatException)
            {
                return false;
            }

            var expectedSignature = ComputeSignature(expiry, passphraseSecret);
            return CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature);
        }

        private static byte[] ComputeSignature(long expiry, string passphraseSecret) =>
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(passphraseSecret), Encoding.UTF8.GetBytes(expiry.ToString()));
    }
}
