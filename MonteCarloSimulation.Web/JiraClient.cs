using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MonteCarloSimulation.Web
{
    public record JiraIssue(string Key, string Url);

    /// <summary>
    /// The only thing in this application that can write to Jira. Deliberately not reachable
    /// by the model - the agent composes fields, this class owns the credentials, the project,
    /// the issue type, and the HTTP call.
    /// </summary>
    public class JiraClient
    {
        // The status the app moves its stories to right after creating them: the Jira rule dispatches agent- stories
        // to GitHub on this transition
        public const string InProgress = "In Progress";

        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        public JiraClient(HttpClient http, IConfiguration config)
        {
            _http = http;
            _config = config;
            _http.BaseAddress = new Uri(BaseUrl + "/");
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{config["Jira:Email"]}:{config["Jira:ApiToken"]}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        private string BaseUrl => (_config["Jira:BaseUrl"] ?? "").TrimEnd('/');

        /// <summary>
        /// Creates the story, with the given labels, in its initial status: one paragraph per line of
        /// <paramref name="description"/>, then <paramref name="codeBlock"/> (if any) as a code block. Moving it on is a
        /// separate call (<see cref="TransitionAsync"/>).
        /// </summary>
        public async Task<JiraIssue> CreateStoryAsync(string summary, string description, IReadOnlyList<string> labels, CancellationToken ct, string? codeBlock = null)
        {
            // Jira Cloud's v3 API rejects a plain string description: it has to be an Atlassian
            // Document Format document, hence the doc/paragraph/text nesting below.
            var content = description
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => (object)new { type = "paragraph", content = new[] { new { type = "text", text = line } } })
                .ToList();
            if (codeBlock is not null)
                content.Add(new { type = "codeBlock", content = new[] { new { type = "text", text = codeBlock } } });

            var payload = new
            {
                fields = new
                {
                    project = new { key = _config["Jira:ProjectKey"] },
                    issuetype = new { name = _config["Jira:IssueType"] },
                    summary,
                    labels,
                    description = new { type = "doc", version = 1, content },
                },
            };

            using var request = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("rest/api/3/issue", request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Jira returned {(int)response.StatusCode}: {body}");

            var key = JsonDocument.Parse(body).RootElement.GetProperty("key").GetString()!;
            return new JiraIssue(key, $"{BaseUrl}/browse/{key}");
        }

        /// <summary>
        /// Moves the story to <paramref name="status"/>. Transition IDs differ between workflows, so the transition is
        /// looked up by its name or the status it leads to (ignoring case); throws if there's no such transition from
        /// the story's current status.
        /// </summary>
        public async Task TransitionAsync(string issueKey, string status, CancellationToken ct)
        {
            using var listResponse = await _http.GetAsync($"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions", ct);
            var listBody = await listResponse.Content.ReadAsStringAsync(ct);
            if (!listResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Jira returned {(int)listResponse.StatusCode} listing {issueKey}'s transitions: {listBody}");

            string? id = null;
            foreach (var transition in JsonDocument.Parse(listBody).RootElement.GetProperty("transitions").EnumerateArray())
            {
                bool matches = string.Equals(transition.GetProperty("name").GetString(), status, StringComparison.OrdinalIgnoreCase)
                    || (transition.TryGetProperty("to", out var to)
                        && string.Equals(to.GetProperty("name").GetString(), status, StringComparison.OrdinalIgnoreCase));
                if (matches)
                {
                    id = transition.GetProperty("id").GetString();
                    break;
                }
            }
            if (id is null)
                throw new InvalidOperationException($"{issueKey} has no transition to \"{status}\" from its current status.");

            var payload = JsonSerializer.Serialize(new { transition = new { id } });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions", content, ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Jira returned {(int)response.StatusCode} moving {issueKey} to {status}: {await response.Content.ReadAsStringAsync(ct)}");
        }
    }
}
