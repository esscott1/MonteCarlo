namespace MonteCarloSimulation.Core
{
    public class SimulationParameters
    {
        public int Years { get; set; }
        public int Iterations { get; set; }
        public double Withdrawal { get; set; }
        public DateOnly Birthdate { get; set; }
        public double InitialTaxableBalance { get; set; }
        public double InitialRothBasis { get; set; }
        public double InitialRothUnrealizedGain { get; set; }
        public double InitialBrokerageBasis { get; set; }
        public double InitialBrokerageUnrealizedGain { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double NewMoney { get; set; }
        public int YearNewMoney { get; set; }
        public int SocialSecurityYearsUntilStart { get; set; }
        public double SocialSecurityAnnualAmount { get; set; }
        public double AnnualStandardDeduction { get; set; }
        public bool EnableRothConversions { get; set; }
        public required string ScenarioDescription { get; set; }
    }
}
