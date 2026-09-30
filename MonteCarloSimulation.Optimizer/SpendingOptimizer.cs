using System.Collections.Concurrent;
using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // Finds, per investment scenario, the annual spending that survives 80-85% of simulated market paths, and the
    // Social Security claiming age (each birthday 62-70) that allows the most of it.
    //
    // For each (scenario, claiming age), every path's break-even spend - the most it survives - is found by
    // bisection. A spend W then survives exactly the share of paths whose break-even is at least W, so the spend
    // for any survival target is read straight off the sorted break-evens: no separate search per target.
    public static class SpendingOptimizer
    {
        public const int DefaultPaths = 500;
        public const double HighSurvival = 0.85;
        public const double MidpointSurvival = 0.825;
        public const double LowSurvival = 0.80;

        public static OptimizationResult Optimize(OptimizationInputs inputs)
        {
            var template = inputs.Template;
            var curve = inputs.SocialSecurity;
            var ages = Enumerable.Range(SocialSecurityCurve.EarliestAge, SocialSecurityCurve.LatestAge - SocialSecurityCurve.EarliestAge + 1).ToList();
            var jobs = inputs.Scenarios.SelectMany(scenario => ages.Select(age => (Scenario: scenario, Age: age))).ToList();

            var byJob = new ConcurrentDictionary<(int ScenarioId, int Age), ClaimingAgeResult>();
            Parallel.ForEach(jobs, job =>
            {
                var start = template.Birthdate.AddYears(job.Age);
                double monthly = curve.MonthlyBenefitAtAge(job.Age);
                var simulator = new PathSimulator(template, job.Scenario, start, monthly);

                var breakEvens = new double[inputs.Paths];
                for (int path = 0; path < inputs.Paths; path++)
                    breakEvens[path] = simulator.BreakEven(path);
                Array.Sort(breakEvens);

                byJob[(job.Scenario.Id, job.Age)] = new ClaimingAgeResult(
                    job.Age, start, monthly, simulator.TotalSocialSecurity,
                    SpendAtSurvival(breakEvens, HighSurvival),
                    SpendAtSurvival(breakEvens, MidpointSurvival),
                    SpendAtSurvival(breakEvens, LowSurvival));
            });

            var scenarios = inputs.Scenarios.Select(scenario =>
            {
                var claimingAges = ages.Select(age => byJob[(scenario.Id, age)]).ToList();

                // Highest midpoint spend; ties go to the earlier age.
                var recommended = claimingAges[0];
                foreach (var candidate in claimingAges.Skip(1))
                    if (candidate.SpendAtMidpoint > recommended.SpendAtMidpoint) recommended = candidate;

                var check = new PathSimulator(template, scenario, recommended.StartDate, recommended.MonthlyBenefit);
                int survivors = Enumerable.Range(0, inputs.Paths).Count(path => check.Survives(recommended.SpendAtMidpoint, path));

                return new ScenarioOptimum(scenario.Id, scenario.Description, recommended, (double)survivors / inputs.Paths, claimingAges);
            }).ToList();

            var benefitByAge = ages.Select(age => new BenefitAtAge(age, curve.MonthlyBenefitAtAge(age))).ToList();
            return new OptimizationResult(scenarios, benefitByAge, inputs.Paths);
        }

        // The largest spend that at least `survival` of the paths survive, given their break-evens sorted ascending:
        // a spend survives every path whose break-even is at least it, so it's the break-even that leaves
        // ceil(survival * n) paths at or above it.
        internal static double SpendAtSurvival(double[] sortedBreakEvens, double survival)
        {
            int n = sortedBreakEvens.Length;
            int survivorsNeeded = (int)Math.Ceiling(survival * n - 1e-9);
            int index = Math.Clamp(n - survivorsNeeded, 0, n - 1);
            return sortedBreakEvens[index];
        }
    }
}
