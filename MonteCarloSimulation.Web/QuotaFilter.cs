namespace MonteCarloSimulation.Web
{
    // Applies a RequestQuota to an endpoint, keyed by the caller's address. Every attempt counts - including invalid
    // input and wrong passphrases - and it runs before the handler, so nothing reaches a paid API or Jira once the
    // limit is used. Every response reports the allowance in headers; a refusal is a 429 with Retry-After and the
    // exact time in the body, which the pages turn into "you can submit again at 4:52 PM".
    public sealed class QuotaFilter(RequestQuota quota, string refusedMessage) : IEndpointFilter
    {
        public const string LimitHeader = "X-Quota-Limit";
        public const string RemainingHeader = "X-Quota-Remaining";
        public const string NextSlotHeader = "X-Quota-Next-Slot-At";

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var decision = quota.TryAcquire(http.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            var headers = http.Response.Headers;
            headers[LimitHeader] = quota.Limit.ToString();
            headers[RemainingHeader] = decision.Remaining.ToString();
            headers[NextSlotHeader] = decision.NextSlotAt.UtcDateTime.ToString("O");

            if (!decision.Allowed)
            {
                headers.RetryAfter = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString();
                return Results.Json(
                    new { message = refusedMessage, retryAt = decision.NextSlotAt.UtcDateTime, limit = quota.Limit },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            return await next(context);
        }
    }
}
