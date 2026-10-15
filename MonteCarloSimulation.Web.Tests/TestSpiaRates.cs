using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.Web.Tests
{
    // The SPIA payout index for test apps that run the Optimizer as a visitor with the annuity feature (its purchase
    // search reads today's rate): a fetch that always fails, so the app quietly uses its committed seed figures - never
    // lifeannuities.us - and a store file in the temp folder rather than the project's App_Data.
    public static class TestSpiaRates
    {
        public static void Use(IWebHostBuilder builder)
        {
            builder.UseSetting("SpiaRates:Path",
                Path.Combine(Path.GetTempPath(), "montecarlo-tests", $"spia-{Guid.NewGuid():N}.json"));
            builder.ConfigureTestServices(services => services.AddHttpClient(SpiaPayoutIndex.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new Unreachable())
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan));
        }

        // The purchase options /api/optimal gives the Optimizer for this request, from the app's own figures
        public static async Task<AnnuityPurchaseOptions?> PurchaseFor(WebApplicationFactory<Program> app, OptimalRequest request) =>
            request.PurchaseOptions(await app.Services.GetRequiredService<SpiaPayoutIndex>().CurrentAsync());

        private sealed class Unreachable : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Tests don't reach lifeannuities.us");
        }
    }
}
