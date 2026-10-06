using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MonteCarloSimulation.Web.Tests
{
    // The change-request flow up to Jira: the story is created with the agent-title-change label and moved straight to
    // In Progress (which is what dispatches it to the agent workflows). The Anthropic call is stood in for, and Jira is a
    // fake that records every request.
    public class ChangeRequestFlowTests
    {
        private const string Passphrase = "test-passphrase";

        private sealed class FakeAgent(IConfiguration config) : ChangeRequestAgent(config)
        {
            public override Task<AgentStory> ComposeStoryAsync(string summary, string description, string timestamp, CancellationToken ct) =>
                Task.FromResult(new AgentStory($"{summary} {timestamp}", DescriptionPrefix + description, false));
        }

        private sealed class FakeJira(string transitionsJson) : HttpMessageHandler
        {
            public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                string path = request.RequestUri!.AbsolutePath;
                lock (Requests) Requests.Add((request.Method, path, body));

                if (request.Method == HttpMethod.Post && path.EndsWith("/rest/api/3/issue"))
                    return Json(HttpStatusCode.Created, """{ "id": "10099", "key": "SCRUM-99" }""");
                if (request.Method == HttpMethod.Get && path.EndsWith("/transitions"))
                    return Json(HttpStatusCode.OK, transitionsJson);
                if (request.Method == HttpMethod.Post && path.EndsWith("/transitions"))
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
                new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private const string WorkflowTransitions = """
            { "transitions": [
                { "id": "11", "name": "To Do", "to": { "name": "To Do" } },
                { "id": "21", "name": "In Progress", "to": { "name": "In Progress" } },
                { "id": "31", "name": "In Review", "to": { "name": "In Review" } } ] }
            """;

        private static WebApplicationFactory<Program> App(FakeJira jira) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ChangeRequest:Passphrase", Passphrase);
                builder.UseSetting("Anthropic:ApiKey", "test-key");
                builder.UseSetting("Jira:ApiToken", "test-token");
                builder.UseSetting("Jira:Email", "test@example.com");
                builder.UseSetting("Jira:BaseUrl", "https://jira.example.test");
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<ChangeRequestAgent>(sp => new FakeAgent(sp.GetRequiredService<IConfiguration>()));
                    services.AddHttpClient<JiraClient>().ConfigurePrimaryHttpMessageHandler(() => jira);
                });
            });

        private static async Task<HttpResponseMessage> Submit(WebApplicationFactory<Program> app) =>
            await app.CreateClient().PostAsync("/api/change-request", new StringContent(
                JsonSerializer.Serialize(new { summary = "Rename the runner", description = "Portfolio Runner", passphrase = Passphrase }),
                Encoding.UTF8, "application/json"));

        [Fact]
        public async Task TheStory_IsLabelledAgentTitleChange_AndMovedToInProgress()
        {
            var jira = new FakeJira(WorkflowTransitions);
            using var app = App(jira);

            using var response = await Submit(app);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("SCRUM-99", result.RootElement.GetProperty("issueKey").GetString());

            Assert.Collection(jira.Requests,
                create =>
                {
                    Assert.Equal(HttpMethod.Post, create.Method);
                    Assert.EndsWith("/rest/api/3/issue", create.Path);
                    using var body = JsonDocument.Parse(create.Body);
                    var fields = body.RootElement.GetProperty("fields");
                    Assert.Equal(new[] { StoryLabels.TitleChange }, fields.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
                    Assert.StartsWith("Rename the runner ", fields.GetProperty("summary").GetString());
                },
                list =>
                {
                    Assert.Equal(HttpMethod.Get, list.Method);
                    Assert.EndsWith("/rest/api/3/issue/SCRUM-99/transitions", list.Path);
                },
                move =>
                {
                    Assert.Equal(HttpMethod.Post, move.Method);
                    Assert.EndsWith("/rest/api/3/issue/SCRUM-99/transitions", move.Path);
                    using var body = JsonDocument.Parse(move.Body);
                    Assert.Equal("21", body.RootElement.GetProperty("transition").GetProperty("id").GetString());
                });
        }

        [Fact]
        public async Task IfTheStoryCantBeMoved_TheVisitorStillGetsItsKey()
        {
            var jira = new FakeJira("""{ "transitions": [ { "id": "31", "name": "In Review", "to": { "name": "In Review" } } ] }""");
            using var app = App(jira);

            using var response = await Submit(app);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("SCRUM-99", result.RootElement.GetProperty("issueKey").GetString());
            Assert.DoesNotContain(jira.Requests, r => r.Method == HttpMethod.Post && r.Path.EndsWith("/transitions"));
        }

        [Fact]
        public async Task TransitionAsync_FindsTheTransition_ByTheStatusItLeadsTo_IgnoringCase()
        {
            var jira = new FakeJira("""{ "transitions": [ { "id": "5", "name": "Start work", "to": { "name": "In Progress" } } ] }""");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jira:BaseUrl"] = "https://jira.example.test",
                ["Jira:Email"] = "test@example.com",
                ["Jira:ApiToken"] = "test-token",
            }).Build();
            var client = new JiraClient(new HttpClient(jira), config);

            await client.TransitionAsync("SCRUM-7", "in progress", CancellationToken.None);

            var move = Assert.Single(jira.Requests, r => r.Method == HttpMethod.Post);
            using var body = JsonDocument.Parse(move.Body);
            Assert.Equal("5", body.RootElement.GetProperty("transition").GetProperty("id").GetString());
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.TransitionAsync("SCRUM-7", "Done", CancellationToken.None));
        }
    }
}
