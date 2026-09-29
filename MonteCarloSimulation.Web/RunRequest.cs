using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web
{
    public class RunRequest
    {
        public int ScenarioId { get; set; }
        public int Years { get; set; }
        public int Iterations { get; set; }
        public double Withdrawal { get; set; }
        public DateOnly Birthdate { get; set; }
        public double InitialTaxableBalance { get; set; }
        public double InitialRothBasis { get; set; }
        public double InitialRothUnrealizedGain { get; set; }
        public double InitialBrokerageBasis { get; set; }
        public double InitialBrokerageUnrealizedGain { get; set; }
        public double NewMoney { get; set; }
        public int YearNewMoney { get; set; }
        public int SocialSecurityYearsUntilStart { get; set; }
        public double SocialSecurityAnnualAmount { get; set; }
        public double AnnualStandardDeduction { get; set; }
        public bool EnableRothConversions { get; set; }

        public Dictionary<string, string> Validate()
        {
            var errors = new Dictionary<string, string>();
            if (InvestmentScenarios.ById(ScenarioId) is null) errors["scenarioId"] = "Select a valid investment scenario.";
            if (Years <= 0) errors["years"] = "Years must be a positive integer.";
            if (Iterations <= 0) errors["iterations"] = "Iterations must be a positive integer.";
            if (Withdrawal < 0) errors["withdrawal"] = "Withdrawal must be non-negative.";
            if (Birthdate == default || Birthdate > DateOnly.FromDateTime(DateTime.Today) || Birthdate < DateOnly.FromDateTime(DateTime.Today).AddYears(-120))
                errors["birthdate"] = "Birthdate must be a valid date in the past.";
            if (InitialTaxableBalance < 0) errors["initialTaxableBalance"] = "Initial taxable balance must be non-negative.";
            if (InitialRothBasis < 0) errors["initialRothBasis"] = "Initial Roth basis must be non-negative.";
            if (InitialRothUnrealizedGain < 0) errors["initialRothUnrealizedGain"] = "Initial Roth unrealized gain must be non-negative.";
            if (InitialBrokerageBasis < 0) errors["initialBrokerageBasis"] = "Initial Brokerage basis must be non-negative.";
            if (InitialBrokerageUnrealizedGain < 0) errors["initialBrokerageUnrealizedGain"] = "Initial Brokerage unrealized gain must be non-negative.";
            if (NewMoney < 0) errors["newMoney"] = "New money must be non-negative.";
            if (YearNewMoney < 0) errors["yearNewMoney"] = "Year of new money must be non-negative.";
            if (SocialSecurityYearsUntilStart < 0) errors["socialSecurityYearsUntilStart"] = "Must be non-negative.";
            if (SocialSecurityAnnualAmount < 0) errors["socialSecurityAnnualAmount"] = "Must be non-negative.";
            if (AnnualStandardDeduction < 0) errors["annualStandardDeduction"] = "Must be non-negative.";
            return errors;
        }
    }
}
