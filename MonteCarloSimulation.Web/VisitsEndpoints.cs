namespace MonteCarloSimulation.Web
{
    // The Observe page's Visits section. POST counts a visit: visit.js sends it once per browser session from the public
    // pages, anonymously, rate-limited per address ("visits") so one script can't inflate the counts much. GET is the
    // last 90 days' report and, like the Features section, needs the Observe token in the X-Observe-Token header.
    public static class VisitsEndpoints
    {
        public const string RateLimitPolicy = "visits";

        public static void MapVisits(this WebApplication app)
        {
            app.MapPost("/api/visits", (VisitCounter visits) =>
            {
                visits.Record();
                return Results.NoContent();
            }).RequireRateLimiting(RateLimitPolicy);

            app.MapGet("/api/visits", (HttpContext context, IConfiguration config, VisitCounter visits) =>
                ObserveAccessToken.Allows(context.Request, config)
                    ? Results.Ok(visits.Report())
                    : Results.StatusCode(StatusCodes.Status401Unauthorized));
        }
    }
}
