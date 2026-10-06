namespace MonteCarloSimulation.Core
{
    public class SimulationRunOutput
    {
        public required SimulationResult Result { get; init; }
        public required List<double> AllRates { get; init; }

        // Year-by-year detail of the last run that didn't fail, or null if every run failed. The Scenario
        // runner only displays it when no run failed, in which case it is simply the final run.
        public IReadOnlyList<RunYearDetail>? LastSuccessfulRun { get; init; }

        // The withdrawal order the runs used: the requested one, or the one Automatic chose for these inputs.
        public WithdrawalStrategy WithdrawalStrategy { get; init; }

        // How far conversions filled: the requested target, the one Automatic chose, or None with conversions off.
        public RothConversionTarget RothConversionTarget { get; init; }
    }
}
