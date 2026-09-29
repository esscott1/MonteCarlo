namespace MonteCarloSimulation.Core
{
    public class SimulationRunOutput
    {
        public required SimulationResult Result { get; init; }
        public required List<double> AllRates { get; init; }
        public required string OutOfMoneyMessage { get; init; }

        // Year-by-year detail of the last run that didn't fail, or null if every run failed. Both front
        // ends only display it when no run failed, in which case it is simply the final run.
        public IReadOnlyList<RunYearDetail>? LastSuccessfulRun { get; init; }
    }
}
