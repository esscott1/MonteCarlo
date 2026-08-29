using MonteCarloSimulation.Core;
using MonteCarloSimulation.Web;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

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
        InitialInvestment = request.InitialInvestment,
        Mean = scenario.Mean,
        StdDev = scenario.StdDev,
        NewMoney = request.NewMoney,
        YearNewMoney = request.YearNewMoney,
        SocialSecurityYearsUntilStart = request.SocialSecurityYearsUntilStart,
        SocialSecurityAnnualAmount = request.SocialSecurityAnnualAmount,
        ScenarioDescription = scenario.Description
    };

    var output = MonteCarloEngine.Run(parameters);
    return Results.Ok(new RunResponse(parameters, output));
});

app.Run();

record RunResponse(SimulationParameters Parameters, SimulationRunOutput Output);
