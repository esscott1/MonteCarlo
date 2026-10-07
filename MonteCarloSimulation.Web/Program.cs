using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using MonteCarloSimulation.Core;
using MonteCarloSimulation.Optimizer;
using MonteCarloSimulation.Web;

var builder = WebApplication.CreateBuilder(args);

// Enums (e.g. WithdrawalStrategy) travel as readable names like "TaxOptimized" rather than ordinals.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHttpClient<JiraClient>();
builder.Services.AddSingleton<ChangeRequestAgent>();

// The passphrase forms - change requests, Observe access and translation submissions - allow 5 attempts an hour per caller address, over a
// rolling hour (RequestQuota via QuotaFilter), so a refusal can say exactly when the next attempt is allowed.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddKeyedSingleton(Quotas.ChangeRequest, (services, _) =>
    new RequestQuota(5, TimeSpan.FromHours(1), services.GetRequiredService<TimeProvider>()));
builder.Services.AddKeyedSingleton(Quotas.ObserveAccess, (services, _) =>
    new RequestQuota(5, TimeSpan.FromHours(1), services.GetRequiredService<TimeProvider>()));
builder.Services.AddKeyedSingleton(Quotas.Translations, (services, _) =>
    new RequestQuota(5, TimeSpan.FromHours(1), services.GetRequiredService<TimeProvider>()));
// Access codes allow more: a team testing from one office shares an address, and every attempt counts, right or wrong
builder.Services.AddKeyedSingleton(Quotas.TierAccess, (services, _) =>
    new RequestQuota(20, TimeSpan.FromHours(1), services.GetRequiredService<TimeProvider>()));

// Paid tiers: the release flags ("FeatureManagement"), each tier's features, price and access code ("Tiers"), and what
// a Free visitor's locked inputs are held to ("FreeDefaults"). With the Subscriptions flag off nothing is gated.
builder.Services.AddFeatureManagement();
builder.Services.Configure<TiersOptions>(builder.Configuration.GetSection("Tiers"));
builder.Services.Configure<FreeDefaultsOptions>(builder.Configuration.GetSection("FreeDefaults"));
builder.Services.AddSingleton<IEntitlements, AccessCodeEntitlements>();
builder.Services.AddSingleton<FeatureAccess>();

// The Observe token check runs on every Observe page load and needs no countdown, so it keeps the built-in limiter.
// Partitioned by caller IP and applied as middleware, so it rejects abusive traffic before the endpoint runs.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("observe-verify", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1) }));
});

var app = builder.Build();

// The splash page is the home: "/" serves splash.html, which introduces the app and has tiles to the Scenario runner
// (index.html) and the Optimizer (optimal.html), plus the menu to Model Info/Translations/Observe. Every page keeps
// its file name, so /optimal.html and /index.html still work directly.
var defaultFiles = new DefaultFilesOptions();
defaultFiles.DefaultFileNames.Clear();
defaultFiles.DefaultFileNames.Add("splash.html");
app.UseDefaultFiles(defaultFiles);
app.UseStaticFiles();
app.UseRateLimiter();

app.MapPost("/api/run", async (
    RunRequest request,
    HttpContext context,
    FeatureAccess features,
    IOptionsMonitor<FreeDefaultsOptions> freeDefaults,
    IOptions<JsonOptions> jsonOptions) =>
{
    var validationErrors = request.Validate();
    if (validationErrors.Count > 0)
        return Results.ValidationProblem(validationErrors.ToDictionary(e => e.Key, e => new[] { e.Value }));

    var access = await features.ForAsync(context);
    var locked = FreeTier.LockedInputs(request, access, freeDefaults.CurrentValue);
    if (locked.Count > 0) return FreeTier.Refused(locked);
    FreeTier.SplitAccounts(request, access, freeDefaults.CurrentValue);

    var assetMix = request.ToAssetMix();
    var parameters = new SimulationParameters
    {
        Years = request.Years,
        Iterations = request.Iterations,
        Withdrawal = request.Withdrawal,
        Birthdate = request.Birthdate,
        RetirementDate = request.RetirementDate,
        InitialTaxableBalance = request.InitialTaxableBalance,
        InitialRothBasis = request.InitialRothBasis,
        InitialRothUnrealizedGain = request.InitialRothUnrealizedGain,
        InitialBrokerageBasis = request.InitialBrokerageBasis,
        InitialBrokerageUnrealizedGain = request.InitialBrokerageUnrealizedGain,
        Mean = assetMix.ExpectedReturn,
        StdDev = assetMix.StdDev,
        AssetMix = assetMix,
        NewMoney = request.NewMoney,
        YearNewMoney = request.YearNewMoney,
        SocialSecurityStartDate = request.SocialSecurityStartDate,
        SocialSecurityMonthlyAmount = request.SocialSecurityMonthlyAmount,
        AnnualStandardDeduction = request.AnnualStandardDeduction,
        EnableRothConversions = request.EnableRothConversions,
        ScenarioDescription = request.MixDescription()
    };

    var output = MonteCarloEngine.Run(parameters);
    var response = new RunResponse(parameters, output);
    return access.Can(Features.TaxDetail)
        ? Results.Ok(response)
        : Results.Json(FreeRunView.From(response, jsonOptions.Value.SerializerOptions));
});

// The Optimal page: the annual spending that survives 80-85% of market paths per investment scenario, and the
// Social Security claiming age that allows the most. CPU-heavy (seconds), fully separate from /api/run, so results
// stream back as they're computed (OptimalStream); invalid input still gets a 400 before anything streams.
app.MapPost("/api/optimal", async (
    OptimalRequest request,
    HttpContext context,
    FeatureAccess features,
    IOptionsMonitor<FreeDefaultsOptions> freeDefaults,
    IOptions<JsonOptions> jsonOptions) =>
{
    var validationErrors = request.Validate();
    if (validationErrors.Count > 0)
        return Results.ValidationProblem(validationErrors.ToDictionary(e => e.Key, e => new[] { e.Value }));

    var access = await features.ForAsync(context);
    var locked = FreeTier.LockedInputs(request, access, freeDefaults.CurrentValue);
    if (locked.Count > 0) return FreeTier.Refused(locked);
    FreeTier.SplitAccounts(request, access, freeDefaults.CurrentValue);

    return new OptimalStream(request.ToInputs(), jsonOptions.Value.SerializerOptions);
});

// Checks run cheapest-first: shape, then passphrase, and only then the paid agent call.
app.MapPost("/api/change-request", async (
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

    if (!ChangeRequest.PassphraseMatches(request.Passphrase, config["ChangeRequest:Passphrase"]))
    {
        logger.LogWarning("Change request rejected: incorrect passphrase.");
        return Results.Json(new { message = "Incorrect passphrase." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (string.IsNullOrEmpty(config["Anthropic:ApiKey"]) || string.IsNullOrEmpty(config["Jira:ApiToken"]))
    {
        logger.LogError("Change request cannot run: Anthropic or Jira credentials are not configured.");
        return Results.Json(
            new { message = "Change requests are not configured on this server." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // The model has no clock of its own, so the timestamp is generated here and passed in
    // as trusted input for it to append.
    var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";

    try
    {
        var story = await agent.ComposeStoryAsync(request.Summary, request.Description, timestamp, ct);
        var issue = await jira.CreateStoryAsync(story.Summary, story.Description, [StoryLabels.TitleChange], ct);

        // Straight to In Progress: that move is what makes the Jira rule dispatch the story to the agent workflows, which
        // open a pull request. Merging that PR is the only human approval - there's no triage step in Jira. If the move
        // fails, the story still exists and the visitor still gets its key; moving it by hand starts the agent.
        try
        {
            await jira.TransitionAsync(issue.Key, JiraClient.InProgress, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Created {IssueKey} but couldn't move it to In Progress, so the agent hasn't started.", issue.Key);
        }

        logger.LogInformation("Created Jira story {IssueKey} (server corrected: {Corrected}).", issue.Key, story.ServerCorrected);
        return Results.Ok(new ChangeRequestResponse(
            issue.Key, issue.Url, story.Summary, story.Description, story.ServerCorrected));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Change request failed.");
        return Results.Json(
            new { message = "The change request could not be completed. Please try again." },
            statusCode: StatusCodes.Status502BadGateway);
    }
}).AddEndpointFilter(new QuotaFilter(
    app.Services.GetRequiredKeyedService<RequestQuota>(Quotas.ChangeRequest), "Too many change requests from this address."));

// The Translations page's Submit: a reviewer's Spanish corrections become a Jira story labelled agent-translation-update,
// moved straight to In Progress like change requests, so the agent-translation-update workflow opens a pull request that
// applies exactly these strings to es.json. Checks run cheapest-first: passphrase, then every edit against the current
// es.json. No AI is involved, so only Jira needs credentials.
app.MapPost("/api/translations/proposal", async (
    TranslationProposal request,
    JiraClient jira,
    IConfiguration config,
    IWebHostEnvironment environment,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Passphrase))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["passphrase"] = ["Passphrase is required."] });
    if (!ChangeRequest.PassphraseMatches(request.Passphrase, config["ChangeRequest:Passphrase"]))
    {
        logger.LogWarning("Translation proposal rejected: incorrect passphrase.");
        return Results.Json(new { message = "Incorrect passphrase." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var spanish = await TranslationFiles.ReadSpanishAsync(environment, ct);
    var validationErrors = request.Validate(spanish);
    if (validationErrors.Count > 0)
        return Results.ValidationProblem(validationErrors.ToDictionary(e => e.Key, e => new[] { e.Value }));

    if (string.IsNullOrEmpty(config["Jira:ApiToken"]))
    {
        logger.LogError("Translation proposal cannot run: Jira credentials are not configured.");
        return Results.Json(new { message = "Translation updates are not configured on this server." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";
    var (description, block) = TranslationRules.StoryText(request.Edits, spanish, request.Note);
    string summary = $"Spanish translation update: {request.Edits.Count} string{(request.Edits.Count == 1 ? "" : "s")} {timestamp}";

    try
    {
        var issue = await jira.CreateStoryAsync(summary, description, [StoryLabels.TranslationUpdate], ct, block);
        try
        {
            await jira.TransitionAsync(issue.Key, JiraClient.InProgress, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Created {IssueKey} but couldn't move it to In Progress, so the workflow hasn't started.", issue.Key);
        }

        logger.LogInformation("Created Jira story {IssueKey} with {Count} translation edits.", issue.Key, request.Edits.Count);
        return Results.Ok(new { issueKey = issue.Key, issueUrl = issue.Url, count = request.Edits.Count });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Translation proposal failed.");
        return Results.Json(new { message = "The translation update could not be submitted. Please try again." }, statusCode: StatusCodes.Status502BadGateway);
    }
}).AddEndpointFilter(new QuotaFilter(
    app.Services.GetRequiredKeyedService<RequestQuota>(Quotas.Translations), "Too many translation submissions from this address."));

// Issues a short-lived, stateless signed token gating the Observe dashboard, keyed by the
// same passphrase already used for change requests - no separate secret to provision.
app.MapPost("/api/observe-access", (ObserveAccessRequest request, IConfiguration config, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Passphrase))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["passphrase"] = new[] { "Passphrase is required." } });

    var secret = config["ChangeRequest:Passphrase"];
    if (!ChangeRequest.PassphraseMatches(request.Passphrase, secret))
    {
        logger.LogWarning("Observe access rejected: incorrect passphrase.");
        return Results.Json(new { message = "Incorrect passphrase." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new { token = ObserveAccessToken.Issue(secret!) });
}).AddEndpointFilter(new QuotaFilter(
    app.Services.GetRequiredKeyedService<RequestQuota>(Quotas.ObserveAccess), "Too many attempts from this address."));

// Verifies a token issued above. Pure computation from the token + shared secret - no
// server-side session state - so it works identically no matter which instance handles it.
app.MapGet("/api/observe-access/verify", (HttpContext ctx, IConfiguration config) =>
{
    var token = ctx.Request.Headers["X-Observe-Token"].ToString();
    var secret = config["ChangeRequest:Passphrase"];
    return !string.IsNullOrEmpty(secret) && ObserveAccessToken.IsValid(token, secret)
        ? Results.Ok()
        : Results.StatusCode(StatusCodes.Status401Unauthorized);
}).RequireRateLimiting("observe-verify");

app.MapTierAccess();

app.Run();

record RunResponse(SimulationParameters Parameters, SimulationRunOutput Output);

record ChangeRequestResponse(string IssueKey, string IssueUrl, string Summary, string Description, bool ServerCorrected);

record ObserveAccessRequest(string Passphrase);

// Service keys of the RequestQuota singletons
static class Quotas
{
    public const string ChangeRequest = "change-request";
    public const string ObserveAccess = "observe-access";
    public const string Translations = "translations";
    public const string TierAccess = "tier-access";
}
