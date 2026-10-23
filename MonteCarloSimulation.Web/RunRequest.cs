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
        // The Annuity tab (Plus, its COLA too): an annuity already owned. The monthly payment (the first one: with a COLA
        // the page has already taken its 10% off), the investment amount (its tax basis, unless it's in a tax-deferred
        // account), "spia" (paying from the Retire On date) or "dia" (from AnnuityStartDate, at least two years out), and
        // whether it has the app's COLA (Annuity.ColaIncrease, 2% each January).
        public bool IncludeAnnuity { get; set; }
        public double AnnuityMonthlyAmount { get; set; }
        public double AnnuityInvestment { get; set; }
        public string? AnnuityType { get; set; }
        public DateOnly? AnnuityStartDate { get; set; }
        public bool AnnuityTaxDeferred { get; set; }
        public bool AnnuityCola { get; set; }

        public bool IsDia => string.Equals(AnnuityType, "dia", StringComparison.OrdinalIgnoreCase);

        // The earliest first payment a DIA may have: two years from today, less a day so a visitor whose local date is
        // a day behind the server's isn't refused the page's own minimum
        public static DateOnly EarliestDiaStart(DateOnly today) => today.AddYears(2).AddDays(-1);

        // The engine's annuity, or null when the tab is off. Call on a validated request.
        public Annuity? ToAnnuity() => !IncludeAnnuity ? null : new Annuity
        {
            MonthlyPayment = AnnuityMonthlyAmount,
            FirstPayment = IsDia ? AnnuityStartDate!.Value : RetirementDate,
            Basis = AnnuityTaxDeferred ? 0 : AnnuityInvestment,
            AnnualIncrease = AnnuityCola ? Annuity.ColaIncrease : 0
        };

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
            if (IncludeAnnuity) ValidateAnnuity(errors);
            FilingStatusInput.Validate(this, errors);
            ValidateAssetMix(errors);
            return errors;
        }

        private void ValidateAnnuity(Dictionary<string, string> errors)
        {
            if (AnnuityMonthlyAmount <= 0) errors["annuityMonthlyAmount"] = "The annuity's monthly payment must be positive.";
            if (AnnuityInvestment < 0) errors["annuityInvestment"] = "The investment amount must be non-negative.";
            if (!IsDia && !string.Equals(AnnuityType, "spia", StringComparison.OrdinalIgnoreCase))
                errors["annuityType"] = "The annuity must be a SPIA or a DIA.";
            else if (IsDia && (AnnuityStartDate is not DateOnly start || start < EarliestDiaStart(DateOnly.FromDateTime(DateTime.Today))))
                errors["annuityStartDate"] = "A DIA's payments must start at least 2 years from today.";
        }
    }
}
