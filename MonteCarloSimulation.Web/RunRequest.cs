using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web
{
    public class RunRequest
    {
        public int ScenarioId { get; set; }
        public int Years { get; set; }
        public int Iterations { get; set; }
        public double Withdrawal { get; set; }
        public double InitialTaxableBalance { get; set; }
        public double InitialNontaxableBalance { get; set; }
        public double NewMoney { get; set; }
        public int YearNewMoney { get; set; }
        public int SocialSecurityYearsUntilStart { get; set; }
        public double SocialSecurityAnnualAmount { get; set; }
        public double AnnualStandardDeduction { get; set; }

        public Dictionary<string, string> Validate()
        {
            var errors = new Dictionary<string, string>();
            if (InvestmentScenarios.ById(ScenarioId) is null) errors["scenarioId"] = "Select a valid investment scenario.";
            if (Years <= 0) errors["years"] = "Years must be a positive integer.";
            if (Iterations <= 0) errors["iterations"] = "Iterations must be a positive integer.";
            if (Withdrawal < 0) errors["withdrawal"] = "Withdrawal must be non-negative.";
            if (InitialTaxableBalance < 0) errors["initialTaxableBalance"] = "Initial taxable balance must be non-negative.";
            if (InitialNontaxableBalance < 0) errors["initialNontaxableBalance"] = "Initial nontaxable balance must be non-negative.";
            if (NewMoney < 0) errors["newMoney"] = "New money must be non-negative.";
            if (YearNewMoney < 0) errors["yearNewMoney"] = "Year of new money must be non-negative.";
            if (SocialSecurityYearsUntilStart < 0) errors["socialSecurityYearsUntilStart"] = "Must be non-negative.";
            if (SocialSecurityAnnualAmount < 0) errors["socialSecurityAnnualAmount"] = "Must be non-negative.";
            if (AnnualStandardDeduction < 0) errors["annualStandardDeduction"] = "Must be non-negative.";
            return errors;
        }
    }
}
