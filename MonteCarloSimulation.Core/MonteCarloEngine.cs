namespace MonteCarloSimulation.Core
{
    // Entry point for both front ends: runs every iteration and assembles the output. The year-by-year
    // simulation lives in RunSimulator.
    public static class MonteCarloEngine
    {
        public static SimulationRunOutput Run(SimulationParameters parameters) => Run(parameters, new Random());

        // Seedable entry point, so tests can pin the random sequence.
        internal static SimulationRunOutput Run(SimulationParameters parameters, Random random)
        {
            var strategy = WithdrawalStrategies.For(parameters.WithdrawalStrategy);
            double ageAtStartYears = (DateOnly.FromDateTime(DateTime.Today).DayNumber - parameters.Birthdate.DayNumber) / 365.25;

            var runs = new List<RunSummary>(parameters.Iterations);
            for (int i = 0; i < parameters.Iterations; i++)
                runs.Add(RunSimulator.Simulate(parameters, strategy, ageAtStartYears, random));

            return new SimulationRunOutput
            {
                Result = new SimulationResult { Runs = runs },
                AllRates = runs.SelectMany(r => r.Years.Select(y => y.RateOfReturn)).ToList(),
                OutOfMoneyMessage = FailureTrace.Build(runs),
                LastSuccessfulRun = runs.LastOrDefault(r => !r.Failed)?.Years
            };
        }
    }
}
