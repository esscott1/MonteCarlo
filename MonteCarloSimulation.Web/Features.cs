namespace MonteCarloSimulation.Web
{
    // The paid features. Code asks whether a visitor may use one of these, never which tier they're on: a tier is just
    // the list of features configured for it under "Tiers" in appsettings.json.
    public static class Features
    {
        public const string AccountBasisSplit = "account-basis-split";
        public const string RothConversions = "roth-conversions";
        public const string CustomReturns = "custom-returns";
        public const string StandardDeduction = "standard-deduction";
        public const string Inheritance = "inheritance";
        public const string TaxDetail = "tax-detail";
        public const string OptimizerFull = "optimizer-full";

        public static IReadOnlyList<string> All { get; } =
            [AccountBasisSplit, RothConversions, CustomReturns, StandardDeduction, Inheritance, TaxDetail, OptimizerFull];
    }

    public enum Tier { Free, Plus, Pro }

    public static class TierNames
    {
        // "plus", "Pro"...: a tier's name in any case, never a number
        public static bool TryParse(string? name, out Tier tier)
        {
            foreach (var candidate in Enum.GetValues<Tier>())
            {
                if (string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    tier = candidate;
                    return true;
                }
            }
            tier = Tier.Free;
            return false;
        }
    }

    // The two site-wide switches: whether Plus is offered and whether Pro is. Flipped on the Observe page (SiteFlags);
    // until then each is its default from the "FeatureManagement" section, read by Microsoft.FeatureManagement (an App
    // Service setting such as FeatureManagement__TierPro overrides it). Their names have no dots, so they work as setting
    // names. A tier that's off isn't offered: no badges for it, and its code is refused. Both off is Free Only.
    public static class Flags
    {
        public const string TierPlus = "TierPlus";
        public const string TierPro = "TierPro";

        public static string For(Tier tier) => tier == Tier.Pro ? TierPro : TierPlus;
    }

    // One paid tier's settings ("Tiers:Plus", "Tiers:Pro"). AccessCode is a secret: it comes from user-secrets locally and
    // from App Service settings (Tiers__Plus__AccessCode) in Azure, never from appsettings.json.
    public sealed class TierSettings
    {
        public string PriceLabel { get; set; } = "";
        public string[] Features { get; set; } = [];
        public string? AccessCode { get; set; }
        // What the tier will add that isn't built yet, shown in its upgrade offer (English; the pages translate each by
        // its paywall.highlight.* key while the English matches)
        public string[] Highlights { get; set; } = [];
    }

    public sealed class TiersOptions
    {
        public TierSettings Plus { get; set; } = new();
        public TierSettings Pro { get; set; } = new();

        public TierSettings? For(Tier tier) => tier switch
        {
            Tier.Plus => Plus,
            Tier.Pro => Pro,
            _ => null
        };
    }

    // What a Free visitor's locked inputs are held to ("FreeDefaults"). The standard deductions (single, married filing
    // jointly) must match the pages' defaults (a test checks); a Free visitor enters one total per account, split by
    // these shares.
    public sealed class FreeDefaultsOptions
    {
        public double StandardDeduction { get; set; } = 16_000;
        public double StandardDeductionMarried { get; set; } = 32_000;
        public double BrokerageGainShare { get; set; } = 0.5;
        public double RothBasisShare { get; set; } = 1.0;
        // How many of each run's first years (model years 0, 1, ...) keep the full tax detail for a Free visitor, as a
        // preview of Plus (FreeRunView)
        public int TaxDetailTeaserYears { get; set; } = 3;

        public double StandardDeductionFor(Core.FilingStatus status) =>
            status == Core.FilingStatus.MarriedJoint ? StandardDeductionMarried : StandardDeduction;
    }

    // The asset classes' returns, std. devs and correlation a Free visitor runs with: the pages' defaults (a test checks).
    public static class AssetMixDefaults
    {
        public const double StockReturn = 0.08;
        public const double StockStdDev = 0.19;
        public const double BondReturn = 0.045;
        public const double BondStdDev = 0.04;
        public const double CashReturn = 0.035;
        public const double CashStdDev = 0.01;
        public const double StockBondCorrelation = 0.1;
    }
}
