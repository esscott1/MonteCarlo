namespace MonteCarloSimulation.Web
{
    // The inputs both pages send (RunRequest, OptimalRequest) that paid tiers unlock.
    public interface IPlanInputs
    {
        bool EnableRothConversions { get; set; }
        double NewMoney { get; set; }
        double AnnualStandardDeduction { get; set; }
        double InitialRothBasis { get; set; }
        double InitialRothUnrealizedGain { get; set; }
        double InitialBrokerageBasis { get; set; }
        double InitialBrokerageUnrealizedGain { get; set; }
    }

    // What a visitor without a paid feature may send. The pages hold a Free visitor's locked inputs at these values, so
    // only a hand-made request is refused - with a 403 naming each field, like a validation error.
    public static class FreeTier
    {
        public static Dictionary<string, string> LockedInputs<T>(T request, Access access, FreeDefaultsOptions free)
            where T : AssetMixRequest, IPlanInputs
        {
            var errors = new Dictionary<string, string>();
            if (!access.Can(Features.RothConversions) && request.EnableRothConversions)
                errors["enableRothConversions"] = "Roth conversions are a Plus feature.";
            if (!access.Can(Features.Inheritance) && request.NewMoney != 0)
                errors["newMoney"] = "Inheritance is a Plus feature.";
            if (!access.Can(Features.StandardDeduction) && Math.Abs(request.AnnualStandardDeduction - free.StandardDeduction) > 0.005)
                errors["annualStandardDeduction"] = "Changing the standard deduction is a Plus feature.";
            if (!access.Can(Features.CustomReturns))
            {
                foreach (var (key, value, expected) in new[]
                {
                    ("stockReturn", request.StockReturn, AssetMixDefaults.StockReturn),
                    ("stockStdDev", request.StockStdDev, AssetMixDefaults.StockStdDev),
                    ("bondReturn", request.BondReturn, AssetMixDefaults.BondReturn),
                    ("bondStdDev", request.BondStdDev, AssetMixDefaults.BondStdDev),
                    ("cashReturn", request.CashReturn, AssetMixDefaults.CashReturn),
                    ("cashStdDev", request.CashStdDev, AssetMixDefaults.CashStdDev),
                    ("stockBondCorrelation", request.StockBondCorrelation, AssetMixDefaults.StockBondCorrelation),
                })
                {
                    if (Math.Abs(value - expected) > 1e-9) errors[key] = CustomReturnsMessage;
                }
            }
            return errors;
        }

        public const string CustomReturnsMessage = "Changing returns, std. devs and the correlation is a Plus feature.";

        // A Free visitor enters one total per account, so whatever split the request carries, the server splits each total
        // by the FreeDefaults shares: Brokerage into unrealized gain and basis, Roth into basis (contributions) and gain.
        public static void SplitAccounts(IPlanInputs request, Access access, FreeDefaultsOptions free)
        {
            if (access.Can(Features.AccountBasisSplit)) return;

            double roth = request.InitialRothBasis + request.InitialRothUnrealizedGain;
            request.InitialRothBasis = roth * Math.Clamp(free.RothBasisShare, 0, 1);
            request.InitialRothUnrealizedGain = roth - request.InitialRothBasis;

            double brokerage = request.InitialBrokerageBasis + request.InitialBrokerageUnrealizedGain;
            request.InitialBrokerageUnrealizedGain = brokerage * Math.Clamp(free.BrokerageGainShare, 0, 1);
            request.InitialBrokerageBasis = brokerage - request.InitialBrokerageUnrealizedGain;
        }

        // A refusal: the same problem shape as a validation error, so the pages show it the same way
        public static IResult Refused(Dictionary<string, string> errors) => Results.ValidationProblem(
            errors.ToDictionary(e => e.Key, e => new[] { e.Value }),
            title: "These inputs need a paid tier.",
            statusCode: StatusCodes.Status403Forbidden);
    }
}
