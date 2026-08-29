namespace MonteCarloSimulation.Core
{
    public class SimulationResult
    {
        public double OutOfMoneyCount { get; set; }
        public List<int> YearsOutOfMoney { get; set; }
        public List<double> FailedScenarioAverages { get; set; }
        public List<double> SuccessMoneyRemaining { get; set; }
        public List<double> EndingBalances { get; set; }
        public List<double> AverageAnnualReturns { get; set; }
        public List<int?> FailureYears { get; set; }
        public List<int> HighestReturnYears { get; set; }
        public List<double> HighestReturnValues { get; set; }
        public List<int> LowestReturnYears { get; set; }
        public List<double> LowestReturnValues { get; set; }
        // Add other result fields as needed
    }
}
