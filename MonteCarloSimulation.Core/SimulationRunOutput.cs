namespace MonteCarloSimulation.Core
{
    public class SimulationRunOutput
    {
        public required SimulationResult Result { get; init; }
        public required List<double> AllRates { get; init; }
        public required string OutOfMoneyMessage { get; init; }
        public List<double>? LastBalances { get; init; }
        public List<double>? LastAnnualReturns { get; init; }
        public List<double>? LastAnnualWithdrawals { get; init; }
        public List<double>? LastTaxableBalances { get; init; }
        public List<double>? LastNontaxableBalances { get; init; }
        public List<double>? LastTaxableWithdrawals { get; init; }
        public List<double>? LastNontaxableWithdrawals { get; init; }
        public List<double>? LastTaxRates { get; init; }
    }
}
