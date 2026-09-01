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
        /// Creates the story and leaves it in its initial status - no transition is applied, so
        /// the Jira webhook that fires on "In Progress" stays quiet until a human moves it.
        /// </summary>
        public async Task<JiraIssue> CreateStoryAsync(string summary, string description, CancellationToken ct)
        {
            // Jira Cloud's v3 API rejects a plain string description: it has to be an Atlassian
            // Document Format document, hence the doc/paragraph/text nesting below.
            var payload = new
            {
                fields = new
                {
                    project = new { key = _config["Jira:ProjectKey"] },
                    issuetype = new { name = _config["Jira:IssueType"] },
                    summary,
                    description = new
                    {
                        type = "doc",
                        version = 1,
                        content = new[]
                        {
                            new
                            {
                                type = "paragraph",
                                content = new[] { new { type = "text", text = description } },
                            },
                        },
                    },
                },
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("rest/api/3/issue", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Jira returned {(int)response.StatusCode}: {body}");

            var key = JsonDocument.Parse(body).RootElement.GetProperty("key").GetString()!;
            return new JiraIssue(key, $"{BaseUrl}/browse/{key}");
        }
    }
}
