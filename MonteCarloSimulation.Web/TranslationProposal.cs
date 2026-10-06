using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MonteCarloSimulation.Web
{
    /// <summary>
    /// A reviewer's Spanish corrections from the Translations page: new text for existing es.json keys. Validated here,
    /// then carried in a Jira story (labelled agent-translation-update) to the agent-translation-update workflow, whose
    /// script (.github/scripts/apply-translation-update.mjs) checks the same rules again before changing es.json.
    /// </summary>
    public class TranslationProposal
    {
        public const int MaxEdits = 100;
        public const int MaxValueLength = 1000;
        public const int MaxNoteLength = 500;

        // The encoded block must leave room in the Jira description (32,767 characters) for the readable list
        public const int MaxPayloadBytes = 12_000;

        public Dictionary<string, string> Edits { get; set; } = [];
        public string Note { get; set; } = "";
        public string Passphrase { get; set; } = "";

        /// <summary>Errors by field ("edits", "edits[key]", "note", "passphrase"); empty when the proposal is valid.</summary>
        public Dictionary<string, string> Validate(IReadOnlyDictionary<string, string> currentSpanish)
        {
            var errors = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(Passphrase)) errors["passphrase"] = "Passphrase is required.";
            if (Note.Length > MaxNoteLength) errors["note"] = $"The note must be {MaxNoteLength} characters or fewer.";

            if (Edits.Count == 0) errors["edits"] = "There are no changes to submit.";
            else if (Edits.Count > MaxEdits) errors["edits"] = $"Submit at most {MaxEdits} changes at a time.";
            else if (Encoding.UTF8.GetByteCount(TranslationRules.PayloadJson(Edits)) > MaxPayloadBytes)
                errors["edits"] = "These changes are too long to submit together; submit them in smaller groups.";

            foreach (var (key, value) in Edits)
            {
                string? problem = currentSpanish.TryGetValue(key, out var current)
                    ? TranslationRules.Check(value, current)
                    : "isn't a translation key on this site.";
                if (problem is not null) errors[$"edits[{key}]"] = $"{key} {problem}";
            }
            return errors;
        }
    }

    public static class TranslationFiles
    {
        /// <summary>The Spanish the site serves now (wwwroot/i18n/es.json), which edits are checked against.</summary>
        public static async Task<Dictionary<string, string>> ReadSpanishAsync(IWebHostEnvironment environment, CancellationToken ct)
        {
            var file = environment.WebRootFileProvider.GetFileInfo("i18n/es.json");
            await using var stream = file.CreateReadStream();
            return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: ct) ?? [];
        }
    }

    /// <summary>
    /// The rules every new Spanish string must follow, and the encoding that carries the edits through Jira. The workflow
    /// script implements the same rules and decoding; TranslationTests and apply-translation-update.test.mjs pin both.
    /// </summary>
    public static partial class TranslationRules
    {
        // The edits travel in the story's description as this prefix plus base64url (no padding) of the payload JSON -
        // characters Jira's rendering can't alter
        public const string PayloadPrefix = "translation-update:v1:";

        private static readonly string[] AllowedTags = ["<strong>", "</strong>", "<em>", "</em>"];

        [GeneratedRegex(@"\{(\w+)\}")]
        private static partial Regex Placeholder();

        [GeneratedRegex(@"<[^<>]*>")]
        private static partial Regex Tag();

        /// <summary>Why <paramref name="value"/> can't replace <paramref name="current"/>, or null if it can.</summary>
        public static string? Check(string value, string current)
        {
            if (string.IsNullOrWhiteSpace(value)) return "can't be empty.";
            if (value.Length > TranslationProposal.MaxValueLength) return $"must be {TranslationProposal.MaxValueLength} characters or fewer.";
            if (value == current) return "is unchanged.";
            if (!Placeholders(value).SequenceEqual(Placeholders(current)))
                return $"must keep the same {{placeholders}} as the current text ({Describe(Placeholders(current))}).";

            // Markup: <strong> and <em>, or a tag the current text already has; no other < or >
            var currentTags = Tag().Matches(current).Select(m => m.Value).ToHashSet();
            foreach (Match tag in Tag().Matches(value))
            {
                if (!AllowedTags.Contains(tag.Value) && !currentTags.Contains(tag.Value))
                    return $"can only use <strong> and <em> for formatting (found {tag.Value}).";
            }
            if (Tag().Replace(value, "").IndexOfAny(['<', '>']) >= 0) return "can't contain < or > outside a formatting tag.";
            return null;
        }

        public static IReadOnlyList<string> Placeholders(string text) =>
            Placeholder().Matches(text).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal).ToList();

        private static string Describe(IReadOnlyList<string> names) =>
            names.Count == 0 ? "none" : string.Join(", ", names.Select(n => $"{{{n}}}"));

        // Accents, dashes and tags stay as themselves rather than \uXXXX escapes: smaller, and the same JSON as
        // JavaScript's JSON.stringify. Safe because the JSON is only ever base64-encoded, never embedded in HTML.
        private static readonly JsonSerializerOptions PayloadOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>The payload JSON: { "version": 1, "edits": { key: value } }, keys in order.</summary>
        public static string PayloadJson(IReadOnlyDictionary<string, string> edits) =>
            JsonSerializer.Serialize(new
            {
                version = 1,
                edits = new SortedDictionary<string, string>(edits.ToDictionary(), StringComparer.Ordinal),
            }, PayloadOptions);

        public static string EncodePayload(IReadOnlyDictionary<string, string> edits) =>
            PayloadPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(PayloadJson(edits)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>The story's description text (one paragraph per line) and the encoded block for its code block.</summary>
        public static (string Description, string Block) StoryText(IReadOnlyDictionary<string, string> edits, IReadOnlyDictionary<string, string> currentSpanish, string note)
        {
            var text = new StringBuilder();
            text.AppendLine("Spanish translation update submitted from the Translations page.");
            if (!string.IsNullOrWhiteSpace(note)) text.AppendLine($"Reviewer's note: {note.Trim()}");
            text.AppendLine($"Changes ({edits.Count}):");
            // The readable list stops at MaxListLength so the description stays within Jira's limit with the block
            int listed = 0;
            foreach (var (key, value) in edits.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                string line = $"{key}: \"{Clip(currentSpanish[key])}\" → \"{Clip(value)}\"";
                if (text.Length + line.Length > MaxListLength) break;
                text.AppendLine(line);
                listed++;
            }
            if (listed < edits.Count) text.AppendLine($"...and {edits.Count - listed} more, all in the block below.");
            text.AppendLine("The agent-translation-update workflow applies exactly the changes encoded in the block below to es.json. Don't edit it.");
            return (text.ToString(), EncodePayload(edits));
        }

        // Jira's description limit is 32,767 characters; the block is at most 16,000 (12,000 bytes in base64)
        private const int MaxListLength = 14_000;

        private static string Clip(string text) => text.Length <= 160 ? text : text[..157] + "...";
    }
}
