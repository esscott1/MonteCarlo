using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;
using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.Web
{
    // The Optimal page's results, streamed as they're computed: newline-delimited JSON, one event per line, flushed as
    // each is written. A run is `start`, then `progress` after each claiming age and `scenario` as each scenario
    // finishes, then `done` - or `error` if the computation fails after the stream has started. If the client
    // disconnects, the request's token stops the optimizer.
    public sealed class OptimalStream(OptimizationInputs inputs, JsonSerializerOptions jsonOptions) : IResult
    {
        public const string ContentType = "application/x-ndjson";

        private static readonly byte[] NewLine = "\n"u8.ToArray();

        public async Task ExecuteAsync(HttpContext context)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<OptimalStream>>();
            var cancellationToken = context.RequestAborted;
            var response = context.Response;
            response.ContentType = ContentType;
            response.Headers.CacheControl = "no-cache";
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            // The optimizer reports from worker threads; this request writes the events in the order they arrive
            var events = Channel.CreateUnbounded<OptimalStreamEvent>(new UnboundedChannelOptions { SingleReader = true });
            var listener = new OptimizationListener
            {
                ClaimingAgeDone = (completed, total) => events.Writer.TryWrite(new OptimalProgressEvent(completed, total)),
                ScenarioDone = scenario => events.Writer.TryWrite(new OptimalScenarioEvent(scenario))
            };
            // A dedicated thread, not a thread-pool one: the pool must stay free to write the stream and serve requests
            _ = Task.Factory.StartNew(() =>
            {
                try
                {
                    SpendingOptimizer.Optimize(inputs, listener, cancellationToken);
                    events.Writer.TryWrite(new OptimalDoneEvent());
                    events.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    events.Writer.TryComplete(e);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            try
            {
                await WriteAsync(response, new OptimalStartEvent(
                    inputs.Paths,
                    inputs.Scenarios.Count * SpendingOptimizer.ClaimingAges.Count,
                    inputs.Scenarios.Select(s => new OptimalScenarioInfo(s.Id, s.Description)).ToList(),
                    SpendingOptimizer.BenefitByAge(inputs.SocialSecurity)), cancellationToken);

                await foreach (var optimalEvent in events.Reader.ReadAllAsync(cancellationToken))
                    await WriteAsync(response, optimalEvent, cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Optimal run stopped: the client disconnected.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Optimal run failed.");
                await WriteAsync(response, new OptimalErrorEvent("The calculation failed. Please try again."), CancellationToken.None);
            }
        }

        private async Task WriteAsync(HttpResponse response, OptimalStreamEvent optimalEvent, CancellationToken cancellationToken)
        {
            // The runtime type, so each event's own fields are written
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(optimalEvent, optimalEvent.GetType(), jsonOptions);
            await response.Body.WriteAsync(line, cancellationToken);
            await response.Body.WriteAsync(NewLine, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
    }

    public abstract record OptimalStreamEvent(string Type);

    public sealed record OptimalScenarioInfo(int ScenarioId, string Description);

    // What the page needs before any results: the scenarios to make room for, how many claiming ages will report
    // progress, and the benefit at each age.
    public sealed record OptimalStartEvent(
        int Paths, int TotalClaimingAges, IReadOnlyList<OptimalScenarioInfo> Scenarios, IReadOnlyList<BenefitAtAge> BenefitByAge)
        : OptimalStreamEvent("start");

    public sealed record OptimalProgressEvent(int Completed, int Total) : OptimalStreamEvent("progress");

    public sealed record OptimalScenarioEvent(ScenarioOptimum Scenario) : OptimalStreamEvent("scenario");

    public sealed record OptimalDoneEvent() : OptimalStreamEvent("done");

    public sealed record OptimalErrorEvent(string Message) : OptimalStreamEvent("error");
}
