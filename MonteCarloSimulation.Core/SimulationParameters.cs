namespace MonteCarloSimulation.Core
{
    public class SimulationParameters
    {
        public int Years { get; set; }
        public int Iterations { get; set; }
        public double Withdrawal { get; set; }
        public double InitialTaxableBalance { get; set; }
        public double InitialNontaxableBalance { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double NewMoney { get; set; }
        public int YearNewMoney { get; set; }
        public int SocialSecurityYearsUntilStart { get; set; }
        public double SocialSecurityAnnualAmount { get; set; }
        public double AnnualStandardDeduction { get; set; }
        public required string ScenarioDescription { get; set; }
    }
}
