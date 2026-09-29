using System.Text.Json;
using System.Threading.RateLimiting;
using MonteCarloSimulation.Core;
using MonteCarloSimulation.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<JiraClient>();
builder.Services.AddSingleton<ChangeRequestAgent>();

// Partitioned by caller IP and applied as middleware, so it rejects abusive traffic before
// the endpoint runs - and therefore before anything reaches a paid API or writes to Jira.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("change-request", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1) }));
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapGet("/api/scenarios", () => InvestmentScenarios.All);

app.MapPost("/api/run", (RunRequest request) =>
{
    var validationErrors = request.Validate();
    if (validationErrors.Count > 0)
        return Results.ValidationProblem(validationErrors.ToDictionary(e => e.Key, e => new[] { e.Value }));

    var scenario = InvestmentScenarios.ById(request.ScenarioId)!;
    var parameters = new SimulationParameters
    {
        Years = request.Years,
        Iterations = request.Iterations,
        Withdrawal = request.Withdrawal,
        InitialTaxableBalance = request.InitialTaxableBalance,
        InitialNontaxableBalance = request.InitialNontaxableBalance,
        Mean = scenario.Mean,
        StdDev = scenario.StdDev,
        NewMoney = request.NewMoney,
        YearNewMoney = request.YearNewMoney,
        SocialSecurityYearsUntilStart = request.SocialSecurityYearsUntilStart,
        SocialSecurityAnnualAmount = request.SocialSecurityAnnualAmount,
        AnnualStandardDeduction = request.AnnualStandardDeduction,
        ScenarioDescription = scenario.Description
    };

    var output = MonteCarloEngine.Run(parameters);
    return Results.Ok(new RunResponse(parameters, output));
});

// Checks run cheapest-first: shape, then passphrase, and only then the paid agent call.
// Once the shape check passes, the response streams Server-Sent Events so the UI can show
// which of these steps is currently in flight instead of one static "please wait" message.
// The HTTP status is always 200 from that point on - success/failure is reported in-band via
// the "error"/"done" stage events, since SSE can't change the status code after headers are sent.
app.MapPost("/api/change-request", async (
    HttpContext httpContext,
    ChangeRequest request,
    ChangeRequestAgent agent,
    JiraClient jira,
    IConfiguration config,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var validationErrors = request.Validate();
    if (validationErrors.Count > 0)
        return Results.ValidationProblem(validationErrors.ToDictionary(e => e.Key, e => new[] { e.Value }));

    var response = httpContext.Response;
    response.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";

    async Task WriteStageAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload, SseJsonOptions);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    await WriteStageAsync(new { stage = "validating" });

    if (!ChangeRequest.PassphraseMatches(request.Passphrase, config["ChangeRequest:Passphrase"]))
    {
        logger.LogWarning("Change request rejected: incorrect passphrase.");
        await WriteStageAsync(new { stage = "error", message = "Incorrect passphrase." });
        return Results.Empty;
    }

    if (string.IsNullOrEmpty(config["Anthropic:ApiKey"]) || string.IsNullOrEmpty(config["Jira:ApiToken"]))
    {
        logger.LogError("Change request cannot run: Anthropic or Jira credentials are not configured.");
        await WriteStageAsync(new { stage = "error", message = "Change requests are not configured on this server." });
        return Results.Empty;
    }

    // The model has no clock of its own, so the timestamp is generated here and passed in
    // as trusted input for it to append.
    var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";

    try
    {
        await WriteStageAsync(new { stage = "calling-ai" });
        var story = await agent.ComposeStoryAsync(request.Summary, request.Description, timestamp, ct);

        await WriteStageAsync(new { stage = "creating-ticket" });
        var issue = await jira.CreateStoryAsync(story.Summary, story.Description, ct);

        logger.LogInformation("Created Jira story {IssueKey} (server corrected: {Corrected}).", issue.Key, story.ServerCorrected);
        await WriteStageAsync(new
        {
            stage = "done",
            result = new ChangeRequestResponse(issue.Key, issue.Url, story.Summary, story.Description, story.ServerCorrected)
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Change request failed.");
        await WriteStageAsync(new { stage = "error", message = "The change request could not be completed. Please try again." });
    }

    return Results.Empty;
}).RequireRateLimiting("change-request");

app.Run();

record RunResponse(SimulationParameters Parameters, SimulationRunOutput Output);

record ChangeRequestResponse(string IssueKey, string IssueUrl, string Summary, string Description, bool ServerCorrected);

partial class Program
{
    // Matches the camelCase convention ASP.NET Core's Results.Json/Ok use by default, since
    // these SSE payloads are written by hand rather than through that pipeline.
    internal static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);
}
