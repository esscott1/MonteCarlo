using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web
{
    // The pages' filing status choice (Demographics): "single" - also when omitted, so older clients keep working - or
    // "married" (filing jointly). Every tier has it.
    public static class FilingStatusInput
    {
        public const string Message = "Filing status must be Single or Married.";

        public static FilingStatus? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "single" => FilingStatus.Single,
            "married" => FilingStatus.MarriedJoint,
            _ => null
        };

        // A validated request's status
        public static FilingStatus Of(IPlanInputs request) => Parse(request.FilingStatus) ?? FilingStatus.Single;

        public static void Validate(IPlanInputs request, Dictionary<string, string> errors)
        {
            if (Parse(request.FilingStatus) is null) errors["filingStatus"] = Message;
        }
    }
}
