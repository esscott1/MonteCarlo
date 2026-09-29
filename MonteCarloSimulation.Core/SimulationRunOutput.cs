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
        public List<double>? LastBrokerageBalances { get; init; }
        public List<double>? LastRothBalances { get; init; }
        public List<double>? LastTaxableWithdrawals { get; init; }
        public List<double>? LastBrokerageWithdrawals { get; init; }
        public List<double>? LastRothWithdrawals { get; init; }
        public List<double>? LastTaxRates { get; init; }
        public List<double>? LastOrdinaryTaxAmounts { get; init; }
        public List<double>? LastCapitalGainsTaxAmounts { get; init; }
        public List<double>? LastOrdinaryBracketRates { get; init; }
        public List<double?>? LastAmountsUntilNextBracket { get; init; }
        public List<double?>? LastNextBracketRates { get; init; }
        public List<double>? LastRothConversionAmounts { get; init; }
        public List<double>? LastRothConversionTaxes { get; init; }
        public List<double>? LastAgesInYear { get; init; }
        public List<double>? LastTaxableWithdrawalPercents { get; init; }
    }
}
