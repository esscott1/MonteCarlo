namespace MonteCarloSimulation.Core
{
    public enum WithdrawalStrategy
    {
        ProRata,
        TaxOptimized,
        // The app's default: before running, try each order on these inputs and use the one that sustains the
        // spending best (AutomaticWithdrawal). Never reaches the year-by-year loop unresolved.
        Automatic
    }
}
