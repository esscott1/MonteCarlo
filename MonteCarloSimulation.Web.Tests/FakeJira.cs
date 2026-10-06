using System.Net;
using System.Text;

namespace MonteCarloSimulation.Web.Tests
{
    // Stands in for Jira's REST API and records every request: creating an issue returns SCRUM-99, listing transitions
    // returns the given JSON, and a transition succeeds.
    internal sealed class FakeJira(string transitionsJson) : HttpMessageHandler
    {
        // A workflow with the usual statuses
        public const string WorkflowTransitions = """
            { "transitions": [
                { "id": "11", "name": "To Do", "to": { "name": "To Do" } },
                { "id": "21", "name": "In Progress", "to": { "name": "In Progress" } },
                { "id": "31", "name": "In Review", "to": { "name": "In Review" } } ] }
            """;

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
}
