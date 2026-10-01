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
            // The app's Automatic choices expand to every (order, conversion target) pair, up to 8.
            var candidates = AutomaticStrategy.Candidates(template);
            // Each scenario's market paths, drawn once for every spend, claiming age and candidate
            var pathsByScenario = inputs.Scenarios.ToDictionary(s => s.Id, s => PathSimulator.SeededPaths(template, s, inputs.Paths));

            Parallel.ForEach(jobs, job =>
            {
                var start = template.Birthdate.AddYears(job.Age);
                double monthly = curve.MonthlyBenefitAtAge(job.Age);
                byJob[(job.Scenario.Id, job.Age)] = BestCandidate(inputs, job.Scenario, job.Age, start, monthly, candidates, pathsByScenario[job.Scenario.Id]);
            });

            var scenarios = inputs.Scenarios.Select(scenario =>
            {
                var claimingAges = ages.Select(age => byJob[(scenario.Id, age)]).ToList();

                // Highest midpoint spend; ties go to the earlier age.
                var recommended = claimingAges[0];
                foreach (var candidate in claimingAges.Skip(1))
                    if (candidate.SpendAtMidpoint > recommended.SpendAtMidpoint) recommended = candidate;

                var check = new PathSimulator(template, scenario, recommended.StartDate, recommended.MonthlyBenefit,
                    recommended.WithdrawalStrategy, recommended.RothConversionTarget, pathsByScenario[scenario.Id]);
                int survivors = Enumerable.Range(0, inputs.Paths).Count(path => check.Survives(recommended.SpendAtMidpoint, path));

                return new ScenarioOptimum(scenario.Id, scenario.Description, recommended, (double)survivors / inputs.Paths, claimingAges);
            }).ToList();

            var benefitByAge = ages.Select(age => new BenefitAtAge(age, curve.MonthlyBenefitAtAge(age))).ToList();
            return new OptimizationResult(scenarios, benefitByAge, inputs.Paths);
        }

        // How many screened candidates get a full break-even search besides the first.
        internal const int Finalists = 2;

        // The candidate with the highest midpoint spend for one scenario and claiming age. A full break-even search
        // for all 8 candidates would take ~4x today's time, so it's two-stage: the first candidate (Tax-optimized,
        // the 12% line) is searched in full, and its midpoint spend becomes the probe; every other candidate is
        // screened with one run per path at that spend (survivors, then after-tax money left); the top Finalists
        // are searched in full, warm-started from the first candidate's break-evens on the same paths.
        private static ClaimingAgeResult BestCandidate(
            OptimizationInputs inputs, InvestmentScenario scenario, int age, DateOnly start, double monthly,
            IReadOnlyList<(WithdrawalStrategy Order, RothConversionTarget Target)> candidates, double[][] paths)
        {
            ClaimingAgeResult FullSearch((WithdrawalStrategy Order, RothConversionTarget Target) c, double[]? hints, out double[] breakEvens)
            {
                var simulator = new PathSimulator(inputs.Template, scenario, start, monthly, c.Order, c.Target, paths);
                breakEvens = new double[inputs.Paths];
                for (int path = 0; path < inputs.Paths; path++)
                    breakEvens[path] = simulator.BreakEven(path, hints?[path]);
                var sorted = (double[])breakEvens.Clone();
                Array.Sort(sorted);
                return new ClaimingAgeResult(
                    age, start, monthly, simulator.TotalSocialSecurity,
                    SpendAtSurvival(sorted, HighSurvival),
                    SpendAtSurvival(sorted, MidpointSurvival),
                    SpendAtSurvival(sorted, LowSurvival),
                    c.Order, c.Target);
            }

            var best = FullSearch(candidates[0], null, out var firstBreakEvens);
            if (candidates.Count == 1) return best;

            double probe = best.SpendAtMidpoint;
            var finalists = candidates.Skip(1)
                .Select(c =>
                {
                    var simulator = new PathSimulator(inputs.Template, scenario, start, monthly, c.Order, c.Target, paths);
                    int survivors = 0;
                    double afterTaxLeft = 0;
                    for (int path = 0; path < inputs.Paths; path++)
                    {
                        var (survived, left) = simulator.Probe(probe, path);
                        if (!survived) continue;
                        survivors++;
                        afterTaxLeft += left;
                    }
                    return (Candidate: c, Survivors: survivors, AfterTaxLeft: afterTaxLeft);
                })
                .OrderByDescending(s => s.Survivors).ThenByDescending(s => s.AfterTaxLeft)
                .Take(Finalists)
                .ToList();

            foreach (var finalist in finalists)
            {
                var result = FullSearch(finalist.Candidate, firstBreakEvens, out _);
                if (result.SpendAtMidpoint > best.SpendAtMidpoint) best = result;
            }
            return best;
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
