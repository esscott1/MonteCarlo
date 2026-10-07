using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web
{
    public class RunRequest : AssetMixRequest, IPlanInputs
    {
        public int Years { get; set; }
        public int Iterations { get; set; }
        public double Withdrawal { get; set; }
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
        public DateOnly SocialSecurityStartDate { get; set; }
        public double SocialSecurityMonthlyAmount { get; set; }
        public double AnnualStandardDeduction { get; set; }
        public bool EnableRothConversions { get; set; }

        public Dictionary<string, string> Validate()
        {
            var errors = new Dictionary<string, string>();
            if (Years <= 0) errors["years"] = "Years must be a positive integer.";
            if (Iterations <= 0) errors["iterations"] = "Iterations must be a positive integer.";
            if (Withdrawal < 0) errors["withdrawal"] = "Withdrawal must be non-negative.";
            if (Birthdate == default || Birthdate > DateOnly.FromDateTime(DateTime.Today) || Birthdate < DateOnly.FromDateTime(DateTime.Today).AddYears(-120))
                errors["birthdate"] = "Birthdate must be a valid date in the past.";
            else
            {
                if (RetirementDate < new DateOnly(FederalTaxBrackets.Year, 1, 1) || RetirementDate <= Birthdate || RetirementDate > Birthdate.AddYears(100))
                    errors["retirementDate"] = $"Retire On Date must be after your birthdate, no earlier than {FederalTaxBrackets.Year}, and before age 100.";
                if (SocialSecurityStartDate < Birthdate.AddYears(62) || SocialSecurityStartDate > Birthdate.AddYears(70))
                    errors["socialSecurityStartDate"] = "Social Security can start between ages 62 and 70.";
            }
            if (InitialTaxableBalance < 0) errors["initialTaxableBalance"] = "Initial taxable balance must be non-negative.";
            if (InitialRothBasis < 0) errors["initialRothBasis"] = "Initial Roth basis must be non-negative.";
            if (InitialRothUnrealizedGain < 0) errors["initialRothUnrealizedGain"] = "Initial Roth unrealized gain must be non-negative.";
            if (InitialBrokerageBasis < 0) errors["initialBrokerageBasis"] = "Initial Brokerage basis must be non-negative.";
            if (InitialBrokerageUnrealizedGain < 0) errors["initialBrokerageUnrealizedGain"] = "Initial Brokerage unrealized gain must be non-negative.";
            if (NewMoney < 0) errors["newMoney"] = "New money must be non-negative.";
            if (YearNewMoney < 0) errors["yearNewMoney"] = "Year of new money must be non-negative.";
            if (SocialSecurityMonthlyAmount < 0) errors["socialSecurityMonthlyAmount"] = "Must be non-negative.";
            if (AnnualStandardDeduction < 0) errors["annualStandardDeduction"] = "Must be non-negative.";
            FilingStatusInput.Validate(this, errors);
            ValidateAssetMix(errors);
            return errors;
        }
    }
}
