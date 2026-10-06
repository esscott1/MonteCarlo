namespace MonteCarloSimulation.Core
{
    // Entry point for the Scenario runner (POST /api/run): runs every iteration and assembles the output. The year-by-year
    // simulation lives in RunSimulator.
    public static class MonteCarloEngine
    {
        public static SimulationRunOutput Run(SimulationParameters parameters) => Run(parameters, new Random());

        // Seedable entry point, so tests can pin the random sequence.
        internal static SimulationRunOutput Run(SimulationParameters parameters, Random random)
        {
            var timeline = RetirementTimeline.Build(parameters);
            var (order, target) = AutomaticStrategy.Resolve(parameters, timeline);
            var strategy = WithdrawalStrategies.For(order);
            var ceiling = ConversionTargets.CeilingFor(target);

            var runs = new List<RunSummary>(parameters.Iterations);
            for (int i = 0; i < parameters.Iterations; i++)
                runs.Add(RunSimulator.Simulate(parameters, timeline, strategy, random, ceiling, out _));

            return new SimulationRunOutput
            {
                Result = new SimulationResult { Runs = runs },
                AllRates = runs.SelectMany(r => r.Years.Select(y => y.RateOfReturn)).ToList(),
                LastSuccessfulRun = runs.LastOrDefault(r => !r.Failed)?.Years,
                WithdrawalStrategy = order,
                RothConversionTarget = target
            };
        }
    }
}
