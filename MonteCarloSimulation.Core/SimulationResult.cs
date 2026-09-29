namespace MonteCarloSimulation.Core
{
    public class SimulationResult
    {
        // One entry per iteration, in run order.
        public required IReadOnlyList<RunSummary> Runs { get; init; }

        public int OutOfMoneyCount => Runs.Count(r => r.Failed);

        // Failed runs only, in run order.
        public IReadOnlyList<int> YearsOutOfMoney => Runs.Where(r => r.Failed).Select(r => r.FailureYear!.Value).ToList();
        public IReadOnlyList<double> FailedScenarioAverages => Runs.Where(r => r.Failed).Select(r => r.AverageAnnualReturn).ToList();

        // The balance of every year that didn't fail, across every run - including the years before a
        // failure in runs that later fail. One entry per year, not one per run.
        public IReadOnlyList<double> SuccessMoneyRemaining =>
            Runs.SelectMany(r => r.Years.Where(y => y.Year != r.FailureYear).Select(y => y.Balance)).ToList();
    }
}
