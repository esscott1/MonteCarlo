using System.Security.Cryptography;
using System.Text;

namespace MonteCarloSimulation.Web
{
    public class ChangeRequest
    {
        public const int MaxSummaryLength = 200;
        public const int MaxDescriptionLength = 35;

        public string Summary { get; set; } = "";
        public string Description { get; set; } = "";
        public string Passphrase { get; set; } = "";

        public Dictionary<string, string> Validate()
        {
            var errors = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(Summary)) errors["summary"] = "Summary is required.";
            else if (Summary.Length > MaxSummaryLength) errors["summary"] = $"Summary must be {MaxSummaryLength} characters or fewer.";
            if (string.IsNullOrWhiteSpace(Description)) errors["description"] = "Description is required.";
            else if (Description.Length > MaxDescriptionLength) errors["description"] = $"Description must be {MaxDescriptionLength} characters or fewer.";
            if (string.IsNullOrWhiteSpace(Passphrase)) errors["passphrase"] = "Passphrase is required.";
            return errors;
        }

        // Compared as fixed-time hash digests rather than with == so that a wrong
        // guess can't be narrowed down by timing how long the comparison took.
        public static bool PassphraseMatches(string supplied, string? expected)
        {
            if (string.IsNullOrEmpty(expected)) return false;
            var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
            return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
        }
    }
}
