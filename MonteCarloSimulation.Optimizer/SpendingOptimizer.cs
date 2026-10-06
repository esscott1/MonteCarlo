using System.Collections.Concurrent;
using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // Finds the annual spending that survives 80-85% of simulated market paths in the template's market (its asset
    // mix), and the Social Security claiming age (each birthday 62-70) that allows the most of it.
    //
    // For each claiming age, every path's break-even spend - the most it survives - is found by
    // bisection. A spend W then survives exactly the share of paths whose break-even is at least W, so the spend
    // for any survival target is read straight off the sorted break-evens: no separate search per target.
    public static class SpendingOptimizer
    {
        public const int DefaultPaths = 500;

        // The market path counts the Optimal page offers: all of DefaultPaths (the most precise), two-thirds and
        // one-third of it (faster; run time scales with the count). Path i is the same market in each, so a smaller
        // count runs the first paths of the larger one.
        public static IReadOnlyList<int> PathChoices { get; } =
            [DefaultPaths, (int)Math.Round(DefaultPaths * 2 / 3.0), (int)Math.Round(DefaultPaths / 3.0)];
        public const double HighSurvival = 0.85;
        public const double MidpointSurvival = 0.825;
        public const double LowSurvival = 0.80;

        // The Social Security claiming ages compared: each birthday from 62 to 70.
        public static IReadOnlyList<int> ClaimingAges { get; } =
            Enumerable.Range(SocialSecurityCurve.EarliestAge, SocialSecurityCurve.LatestAge - SocialSecurityCurve.EarliestAge + 1).ToList();

        // The monthly benefit at each claiming age, from the user's 62/67/70 amounts.
        public static IReadOnlyList<BenefitAtAge> BenefitByAge(SocialSecurityCurve curve) =>
            ClaimingAges.Select(age => new BenefitAtAge(age, curve.MonthlyBenefitAtAge(age))).ToList();

        public static OptimizationResult Optimize(OptimizationInputs inputs) => Optimize(inputs, null, CancellationToken.None);

        // The same result, reporting as it goes: `listener` hears each finished claiming age (from worker threads), and
        // cancelling the token stops the work, throwing OperationCanceledException.
        public static OptimizationResult Optimize(OptimizationInputs inputs, OptimizationListener? listener, CancellationToken cancellationToken)
        {
            var template = inputs.Template;
            var curve = inputs.SocialSecurity;
            var ages = ClaimingAges;

            var byAge = new ClaimingAgeResult[ages.Count];
            int completed = 0;
            // The app's Automatic choices expand to every (order, conversion target) pair, up to 8.
            var candidates = AutomaticStrategy.Candidates(template);
            // The market's paths, drawn once for every spend, claiming age and candidate
            var paths = PathSimulator.SeededPaths(template, inputs.Paths);

            try
            {
                // NoBuffering hands out the claiming ages one at a time, in order. Capped at one worker per core:
                // uncapped, the loop takes every thread-pool thread it can get, starving the web server's own work
                // (including streaming these results) until it finishes.
                Parallel.ForEach(
                    Partitioner.Create(Enumerable.Range(0, ages.Count), EnumerablePartitionerOptions.NoBuffering),
                    new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
                    i =>
                    {
                        int age = ages[i];
                        var start = template.Birthdate.AddYears(age);
                        double monthly = curve.MonthlyBenefitAtAge(age);
                        byAge[i] = BestCandidate(inputs, age, start, monthly, candidates, paths, cancellationToken);
                        listener?.ClaimingAgeDone?.Invoke(Interlocked.Increment(ref completed), ages.Count);
                    });
            }
            catch (AggregateException e) when (cancellationToken.IsCancellationRequested
                && e.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return new OptimizationResult(OptimumFor(inputs, byAge, paths), BenefitByAge(curve), inputs.Paths);
        }

        // The recommendation from the claiming ages: the highest midpoint spend (ties go to the earlier age),
        // re-simulated on every path to report the share that really survives at that spend.
        private static SpendOptimum OptimumFor(OptimizationInputs inputs, IReadOnlyList<ClaimingAgeResult> claimingAges, double[][] paths)
        {
            var recommended = claimingAges[0];
            foreach (var candidate in claimingAges.Skip(1))
                if (candidate.SpendAtMidpoint > recommended.SpendAtMidpoint) recommended = candidate;

            var check = new PathSimulator(inputs.Template, recommended.StartDate, recommended.MonthlyBenefit,
                recommended.WithdrawalStrategy, recommended.RothConversionTarget, paths);
            int survivors = Enumerable.Range(0, inputs.Paths).Count(path => check.Survives(recommended.SpendAtMidpoint, path));

            return new SpendOptimum(recommended, (double)survivors / inputs.Paths, claimingAges);
        }

        // How many screened candidates get a full break-even search besides the first.
        internal const int Finalists = 2;

        // The candidate with the highest midpoint spend for one claiming age. A full break-even search
        // for all 8 candidates would take ~4x today's time, so it's two-stage: the first candidate (Tax-optimized,
        // the 12% line) is searched in full, and its midpoint spend becomes the probe; every other candidate is
        // screened with one run per path at that spend (survivors, then after-tax money left); the top Finalists
        // are searched in full, warm-started from the first candidate's break-evens on the same paths.
        private static ClaimingAgeResult BestCandidate(
            OptimizationInputs inputs, int age, DateOnly start, double monthly,
            IReadOnlyList<(WithdrawalStrategy Order, RothConversionTarget Target)> candidates, double[][] paths,
            CancellationToken cancellationToken)
        {
            ClaimingAgeResult FullSearch((WithdrawalStrategy Order, RothConversionTarget Target) c, double[]? hints, out double[] breakEvens)
            {
                var simulator = new PathSimulator(inputs.Template, start, monthly, c.Order, c.Target, paths);
                breakEvens = new double[inputs.Paths];
                for (int path = 0; path < inputs.Paths; path++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    breakEvens[path] = simulator.BreakEven(path, hints?[path]);
                }
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
                    var simulator = new PathSimulator(inputs.Template, start, monthly, c.Order, c.Target, paths);
                    int survivors = 0;
                    double afterTaxLeft = 0;
                    for (int path = 0; path < inputs.Paths; path++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
