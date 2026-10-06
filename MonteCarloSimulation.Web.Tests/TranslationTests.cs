using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MonteCarloSimulation.Web.Tests
{
    // The Translations page's Submit: the rules every new Spanish string must follow, the edits block the workflow script
    // decodes, and the endpoint up to Jira (a story labelled agent-translation-update, moved to In Progress).
    public class TranslationTests
    {
        private const string Passphrase = "test-passphrase";

        // apply-translation-update.test.mjs decodes this same string, so the app and the workflow script agree
        [Fact]
        public void EncodePayload_MatchesTheWorkflowScript()
        {
            var edits = new Dictionary<string, string>
            {
                ["optimal.startSs"] = "Empiece el Seguro Social a los {age} años ({date}), \"citado\" — {benefit}/mes, {total} en total.",
                ["info.case.runsOut"] = "Se queda sin dinero <strong>accesible</strong>",
            };

            Assert.Equal(
                "translation-update:v1:eyJ2ZXJzaW9uIjoxLCJlZGl0cyI6eyJpbmZvLmNhc2UucnVuc091dCI6IlNlIHF1ZWRhIHNpbiBkaW5lcm8gPHN0cm9uZz5hY2Nlc2libGU8L3N0cm9uZz4iLCJvcHRpbWFsLnN0YXJ0U3MiOiJFbXBpZWNlIGVsIFNlZ3VybyBTb2NpYWwgYSBsb3Mge2FnZX0gYcOxb3MgKHtkYXRlfSksIFwiY2l0YWRvXCIg4oCUIHtiZW5lZml0fS9tZXMsIHt0b3RhbH0gZW4gdG90YWwuIn19",
                TranslationRules.EncodePayload(edits));
        }

        [Theory]
        [InlineData("Hola {name}, <strong>bienvenido</strong>", "Hola {name}", null)]
        [InlineData("Usa <code>dotnet</code> y <em>listo</em>", "Usa <code>dotnet</code>", null)]
        [InlineData("   ", "Hola", "can't be empty.")]
        [InlineData("Hola", "Hola", "is unchanged.")]
        [InlineData("Hola", "Hola {name}", "must keep the same {placeholders} as the current text ({name}).")]
        [InlineData("Hola {nombre}", "Hola {name}", "must keep the same {placeholders} as the current text ({name}).")]
        [InlineData("<script>alert(1)</script>", "Hola", "can only use <strong> and <em> for formatting (found <script>).")]
        [InlineData("<strong onclick=\"x()\">Hola</strong>", "Hola", "can only use <strong> and <em> for formatting (found <strong onclick=\"x()\">).")]
        [InlineData("5 < 6", "Hola", "can't contain < or > outside a formatting tag.")]
        public void Check_AppliesTheSameRulesAsTheWorkflowScript(string value, string current, string? expected)
        {
            Assert.Equal(expected, TranslationRules.Check(value, current));
        }

        [Fact]
        public void Validate_RejectsUnknownKeys_AndTooManyEdits()
        {
            var spanish = new Dictionary<string, string> { ["a.b"] = "Hola" };

            var unknown = new TranslationProposal { Passphrase = "x", Edits = new() { ["x.y"] = "Nuevo" } }.Validate(spanish);
            Assert.Equal("x.y isn't a translation key on this site.", unknown["edits[x.y]"]);

            var many = new TranslationProposal { Passphrase = "x", Edits = Enumerable.Range(0, 101).ToDictionary(i => $"k{i}", _ => "v") }.Validate(spanish);
            Assert.Equal("Submit at most 100 changes at a time.", many["edits"]);

            Assert.Equal("There are no changes to submit.", new TranslationProposal { Passphrase = "x" }.Validate(spanish)["edits"]);
        }

        [Fact]
        public void StoryText_ListsTheChanges_WithinJirasLimit()
        {
            var spanish = Enumerable.Range(0, 100).ToDictionary(i => $"key.{i:D3}", _ => new string('a', 990));
            var edits = spanish.ToDictionary(e => e.Key, _ => new string('b', 990));

            var (description, block) = TranslationRules.StoryText(edits, spanish, "Revisé todo");

            Assert.Contains("Reviewer's note: Revisé todo", description);
            Assert.Contains("key.000: \"", description);
            Assert.Contains("more, all in the block below.", description);
            Assert.StartsWith(TranslationRules.PayloadPrefix, block);
            Assert.True(description.Length < 15_000, $"description is {description.Length} characters");
        }

        private static WebApplicationFactory<Program> App(FakeJira jira) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ChangeRequest:Passphrase", Passphrase);
                builder.UseSetting("Jira:ApiToken", "test-token");
                builder.UseSetting("Jira:Email", "test@example.com");
                builder.UseSetting("Jira:BaseUrl", "https://jira.example.test");
                builder.ConfigureTestServices(services =>
                    services.AddHttpClient<JiraClient>().ConfigurePrimaryHttpMessageHandler(() => jira));
            });

        private static Task<HttpResponseMessage> Submit(WebApplicationFactory<Program> app, object body) =>
            app.CreateClient().PostAsync("/api/translations/proposal",
                new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

        // A real key from es.json, so the endpoint checks the edit against the file the site serves. The new text is
        // built from whatever the Spanish is now, so the test keeps passing as translation PRs change it.
        private const string Key = "info.case.stillGoing";

        private static async Task<string> NewSpanish(WebApplicationFactory<Program> app)
        {
            using var es = JsonDocument.Parse(await app.CreateClient().GetStringAsync("/i18n/es.json"));
            return es.RootElement.GetProperty(Key).GetString() + " (prueba)";
        }

        [Fact]
        public async Task AValidProposal_BecomesAStoryLabelledAgentTranslationUpdate_MovedToInProgress()
        {
            var jira = new FakeJira(FakeJira.WorkflowTransitions);
            using var app = App(jira);
            string spanish = await NewSpanish(app);

            using var response = await Submit(app, new { passphrase = Passphrase, note = "Más natural", edits = new Dictionary<string, string> { [Key] = spanish } });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("SCRUM-99", result.RootElement.GetProperty("issueKey").GetString());

            var create = jira.Requests[0];
            using var story = JsonDocument.Parse(create.Body);
            var fields = story.RootElement.GetProperty("fields");
            Assert.Equal(new[] { StoryLabels.TranslationUpdate }, fields.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
            Assert.StartsWith("Spanish translation update: 1 string ", fields.GetProperty("summary").GetString());

            // The description's last node is the code block the workflow decodes
            var content = fields.GetProperty("description").GetProperty("content").EnumerateArray().ToList();
            Assert.Equal("codeBlock", content[^1].GetProperty("type").GetString());
            Assert.Equal(TranslationRules.EncodePayload(new Dictionary<string, string> { [Key] = spanish }),
                content[^1].GetProperty("content")[0].GetProperty("text").GetString());
            Assert.Contains(content, node => node.GetProperty("content")[0].GetProperty("text").GetString() == "Reviewer's note: Más natural");

            Assert.Contains(jira.Requests, r => r.Method == HttpMethod.Post && r.Path.EndsWith("/rest/api/3/issue/SCRUM-99/transitions") && r.Body.Contains("\"21\""));
        }

        [Fact]
        public async Task AWrongPassphrase_IsRefused_BeforeAnythingReachesJira()
        {
            var jira = new FakeJira(FakeJira.WorkflowTransitions);
            using var app = App(jira);

            using var response = await Submit(app, new { passphrase = "wrong", edits = new Dictionary<string, string> { [Key] = "Cualquier texto" } });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(jira.Requests);
        }

        [Fact]
        public async Task AnEditThatBreaksTheRules_IsA400_NamingTheKey()
        {
            var jira = new FakeJira(FakeJira.WorkflowTransitions);
            using var app = App(jira);

            using var response = await Submit(app, new { passphrase = Passphrase, edits = new Dictionary<string, string> { [Key] = "<b>Sigue</b>" } });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains("<b>", problem.RootElement.GetProperty("errors").GetProperty($"edits[{Key}]")[0].GetString());
            Assert.Empty(jira.Requests);
        }
    }
}
