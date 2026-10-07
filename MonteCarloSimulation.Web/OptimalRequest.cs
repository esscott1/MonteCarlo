using MonteCarloSimulation.Core;
using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.Web
{
    // Input for the Optimal page: the main page's inputs (the asset mix included) minus the withdrawal (which is solved for), the run count
    // (replaced by Paths, the number of market paths: one of SpendingOptimizer.PathChoices, DefaultPaths when
    // omitted) and the single Social Security amount/date - replaced by the monthly benefit estimates at 62, 67 and 70.
    public class OptimalRequest : AssetMixRequest, IPlanInputs
    {
        public int Years { get; set; }
        public DateOnly Birthdate { get; set; }
        public DateOnly RetirementDate { get; set; }
        // "single" (or omitted) or "married": FilingStatusInput
        public string? FilingStatus { get; set; }
        public double InitialTaxableBalance { get; set; }
        public double InitialRothBasis { get; set; }
        public double InitialRothUnrealizedGain { get; set; }
        public double InitialBrokerageBasis { get; set; }
        public double InitialBrokerageUnrealizedGain { get; set; }
        public double NewMoney { get; set; }
        public int YearNewMoney { get; set; }
        public double SocialSecurityAt62 { get; set; }
        public double SocialSecurityAt67 { get; set; }
        public double SocialSecurityAt70 { get; set; }
        public double AnnualStandardDeduction { get; set; }
        public bool EnableRothConversions { get; set; }
        public int? Paths { get; set; }

        public Dictionary<string, string> Validate()
        {
            var errors = new Dictionary<string, string>();
            if (Years <= 0 || Years > 100) errors["years"] = "Years must be between 1 and 100.";
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (Birthdate == default || Birthdate > today || Birthdate < today.AddYears(-120))
                errors["birthdate"] = "Birthdate must be a valid date in the past.";
            else if (RetirementDate < new DateOnly(FederalTaxBrackets.Year, 1, 1) || RetirementDate <= Birthdate || RetirementDate > Birthdate.AddYears(100))
                errors["retirementDate"] = $"Retire On Date must be after your birthdate, no earlier than {FederalTaxBrackets.Year}, and before age 100.";
            if (InitialTaxableBalance < 0) errors["initialTaxableBalance"] = "Must be non-negative.";
            if (InitialRothBasis < 0) errors["initialRothBasis"] = "Must be non-negative.";
            if (InitialRothUnrealizedGain < 0) errors["initialRothUnrealizedGain"] = "Must be non-negative.";
            if (InitialBrokerageBasis < 0) errors["initialBrokerageBasis"] = "Must be non-negative.";
            if (InitialBrokerageUnrealizedGain < 0) errors["initialBrokerageUnrealizedGain"] = "Must be non-negative.";
            if (NewMoney < 0) errors["newMoney"] = "Must be non-negative.";
            if (YearNewMoney < 0) errors["yearNewMoney"] = "Must be non-negative.";
            if (SocialSecurityAt62 < 0 || SocialSecurityAt67 < 0 || SocialSecurityAt70 < 0)
                errors["socialSecurity"] = "Social Security amounts must be non-negative.";
            else if (SocialSecurityAt62 > SocialSecurityAt67 || SocialSecurityAt67 > SocialSecurityAt70)
                errors["socialSecurity"] = "Social Security amounts should increase with age: 62 <= 67 <= 70.";
            if (AnnualStandardDeduction < 0) errors["annualStandardDeduction"] = "Must be non-negative.";
            FilingStatusInput.Validate(this, errors);
            if (Paths is int paths && !SpendingOptimizer.PathChoices.Contains(paths))
                errors["paths"] = $"Simulated markets must be one of {string.Join(", ", SpendingOptimizer.PathChoices)}.";
            ValidateAssetMix(errors);
            return errors;
        }

        public OptimizationInputs ToInputs()
        {
            var mix = ToAssetMix();
            return new OptimizationInputs
            {
                Template = new SimulationParameters
                {
                    Years = Years,
                    Iterations = 1,
                    Birthdate = Birthdate,
                    RetirementDate = RetirementDate,
                    InitialTaxableBalance = InitialTaxableBalance,
                    InitialRothBasis = InitialRothBasis,
                    InitialRothUnrealizedGain = InitialRothUnrealizedGain,
                    InitialBrokerageBasis = InitialBrokerageBasis,
                    InitialBrokerageUnrealizedGain = InitialBrokerageUnrealizedGain,
                    NewMoney = NewMoney,
                    YearNewMoney = YearNewMoney,
                    FilingStatus = FilingStatusInput.Of(this),
                    AnnualStandardDeduction = AnnualStandardDeduction,
                    EnableRothConversions = EnableRothConversions,
                    Mean = mix.ExpectedReturn,
                    StdDev = mix.StdDev,
                    AssetMix = mix,
                    ScenarioDescription = MixDescription()
                },
                SocialSecurity = new SocialSecurityCurve(SocialSecurityAt62, SocialSecurityAt67, SocialSecurityAt70),
                Paths = Paths ?? SpendingOptimizer.DefaultPaths
            };
        }
    }
}
