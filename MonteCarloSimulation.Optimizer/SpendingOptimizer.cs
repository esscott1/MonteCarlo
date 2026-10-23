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

        // How many screened purchases get a full search when only level Tax Deferred SPIAs are weighed: the top two. With
        // COLAs or after-tax money weighed, it's the best of each kind instead (level or COLA, Tax Deferred or after-tax),
        // so each kind is searched in full: up to four.
        internal const int PurchaseFinalists = 2;

        private static bool ByKind(AnnuityPurchaseOptions options) => options.ConsiderCola || options.ConsiderAfterTax;

        private static int FinalistCount(IReadOnlyList<AnnuityCandidate> candidates, AnnuityPurchaseOptions options) =>
            ByKind(options) ? candidates.Select(c => (c.Funding, c.Cola)).Distinct().Count() : Math.Min(PurchaseFinalists, candidates.Count);

        // How many steps Optimize reports: 9 claiming ages for the plan without a purchase, and with purchases to try,
        // one for screening them and 9 for each finalist.
        public static int TotalSteps(OptimizationInputs inputs)
        {
            if (inputs.Purchase is not { } options) return ClaimingAges.Count;
            var candidates = AnnuityPurchase.Candidates(inputs.Template, options);
            return ClaimingAges.Count * (1 + FinalistCount(candidates, options)) + (candidates.Count > 0 ? 1 : 0);
        }

        // The same result, reporting as it goes: `listener` hears each finished step (from worker threads), and
        // cancelling the token stops the work, throwing OperationCanceledException.
        //
        // With purchase options, the plan without a purchase is searched first (exactly as without options), then every
        // purchase (each size, level and, when COLAs are weighed, with one; with Tax Deferred money and, when weighed, with
        // after-tax money) is screened at that plan's recommended spend -
        // at each claiming age, with that age's order and conversion target, the purchases that let the most paths
        // survive (then the most after-tax money left) - and the PurchaseFinalists are searched in full. The plan with
        // the highest midpoint spend wins; a purchase is recommended only if it beats the plan without one.
        public static OptimizationResult Optimize(OptimizationInputs inputs, OptimizationListener? listener, CancellationToken cancellationToken)
        {
            var template = inputs.Template;
            // The market's paths, drawn once for every spend, claiming age, candidate and purchase
            var paths = PathSimulator.SeededPaths(template, inputs.Paths);
            var options = inputs.Purchase;
            var candidates = options is null ? [] : AnnuityPurchase.Candidates(template, options);
            var progress = new Progress(listener, TotalSteps(inputs));

            try
            {
                var without = Plan(inputs, template, paths, progress, cancellationToken);
                if (options is null || candidates.Count == 0)
                    return new OptimizationResult(without.Optimum, BenefitByAge(inputs.SocialSecurity), inputs.Paths);

                var finalists = ScreenPurchases(inputs, options, candidates, without, paths, cancellationToken);
                progress.Done();

                var best = without;
                AnnuityCandidate? bought = null;
                foreach (var candidate in finalists)
                {
                    var with = Plan(inputs, AnnuityPurchase.Apply(template, options, candidate.Amount, candidate.Cola, candidate.Funding), paths, progress, cancellationToken);
                    if (with.Optimum.Recommended.SpendAtMidpoint > best.Optimum.Recommended.SpendAtMidpoint)
                        (best, bought) = (with, candidate);
                }

                var decision = new AnnuityPurchaseDecision(
                    bought?.Amount ?? 0,
                    bought is null ? 0 : AnnuityPurchase.MonthlyPayment(options, bought.Amount, bought.Cola),
                    options.MonthlyPer100k,
                    options.RateAsOf,
                    without.Optimum.Recommended.SpendAtMidpoint,
                    best.Optimum.Recommended.SpendAtMidpoint,
                    without.MoneyLeft,
                    best.MoneyLeft,
                    candidates.Count,
                    bought is { Cola: true } ? Annuity.ColaIncrease : 0,
                    bought is null ? null : AnnuityPurchase.Source(template, bought.Amount, bought.Funding));
                return new OptimizationResult(best.Optimum with { Purchase = decision }, BenefitByAge(inputs.SocialSecurity), inputs.Paths);
            }
            catch (AggregateException e) when (cancellationToken.IsCancellationRequested
                && e.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        // Counts finished steps for the listener, from any thread
        private sealed class Progress(OptimizationListener? listener, int total)
        {
            private int _completed;
            public void Done() => listener?.StepDone?.Invoke(Interlocked.Increment(ref _completed), total);
        }

        // One plan searched: its recommendation and the median money left at its recommended spend
        private sealed record PlanResult(SpendOptimum Optimum, double MoneyLeft);

        // Every claiming age's best spend for this template (one step each), then its recommendation
        private static PlanResult Plan(
            OptimizationInputs inputs, SimulationParameters template, double[][] paths, Progress progress, CancellationToken cancellationToken)
        {
            var curve = inputs.SocialSecurity;
            var ages = ClaimingAges;
            var byAge = new ClaimingAgeResult[ages.Count];
            // The app's Automatic choices expand to every (order, conversion target) pair, up to 8.
            var candidates = AutomaticStrategy.Candidates(template);

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
                    byAge[i] = BestCandidate(inputs, template, age, start, monthly, candidates, paths, cancellationToken);
                    progress.Done();
                });

            return OptimumFor(inputs, template, byAge, paths);
        }

        // The purchases worth a full search: each one at each claiming age (that age's order and conversion target in the
        // plan without a purchase), every path run at that plan's recommended spend. Ranked by the most paths surviving at
        // any one age, then the after-tax money left there; the top PurchaseFinalists, or with COLAs or after-tax money
        // weighed the best of each kind, in rank order.
        private static IReadOnlyList<AnnuityCandidate> ScreenPurchases(
            OptimizationInputs inputs, AnnuityPurchaseOptions options, IReadOnlyList<AnnuityCandidate> candidates, PlanResult without,
            double[][] paths, CancellationToken cancellationToken)
        {
            double probe = without.Optimum.Recommended.SpendAtMidpoint;
            var ages = without.Optimum.ClaimingAges;
            var scores = new (int Survivors, double AfterTaxLeft)[candidates.Count, ages.Count];

            Parallel.For(0, candidates.Count * ages.Count,
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
                k =>
                {
                    int a = k / ages.Count, i = k % ages.Count;
                    var age = ages[i];
                    var simulator = new PathSimulator(AnnuityPurchase.Apply(inputs.Template, options, candidates[a].Amount, candidates[a].Cola, candidates[a].Funding),
                        age.StartDate, age.MonthlyBenefit, age.WithdrawalStrategy, age.RothConversionTarget, paths);
                    int survivors = 0;
                    double left = 0;
                    for (int path = 0; path < inputs.Paths; path++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var (survived, afterTax) = simulator.Probe(probe, path);
                        if (!survived) continue;
                        survivors++;
                        left += afterTax;
                    }
                    scores[a, i] = (survivors, left);
                });

            var ranked = Enumerable.Range(0, candidates.Count)
                .Select(a => (Candidate: candidates[a], Best: Enumerable.Range(0, ages.Count).Select(i => scores[a, i])
                    .OrderByDescending(s => s.Survivors).ThenByDescending(s => s.AfterTaxLeft).First()))
                .OrderByDescending(x => x.Best.Survivors).ThenByDescending(x => x.Best.AfterTaxLeft)
                .Select(x => x.Candidate)
                .ToList();
            return ByKind(options)
                ? ranked.GroupBy(c => (c.Funding, c.Cola)).Select(kind => kind.First()).ToList()
                : ranked.Take(PurchaseFinalists).ToList();
        }

        // The recommendation from the claiming ages: the highest midpoint spend (ties go to the earlier age),
        // re-simulated on every path to report the share that really survives at that spend and the median after-tax
        // money left (a failed path counting as 0).
        private static PlanResult OptimumFor(
            OptimizationInputs inputs, SimulationParameters template, IReadOnlyList<ClaimingAgeResult> claimingAges, double[][] paths)
        {
            var recommended = claimingAges[0];
            foreach (var candidate in claimingAges.Skip(1))
                if (candidate.SpendAtMidpoint > recommended.SpendAtMidpoint) recommended = candidate;

            var check = new PathSimulator(template, recommended.StartDate, recommended.MonthlyBenefit,
                recommended.WithdrawalStrategy, recommended.RothConversionTarget, paths);
            var left = new double[inputs.Paths];
            int survivors = 0;
            for (int path = 0; path < inputs.Paths; path++)
            {
                var (survived, afterTax) = check.Probe(recommended.SpendAtMidpoint, path);
                if (survived) survivors++;
                left[path] = afterTax;
            }
            Array.Sort(left);
            int n = inputs.Paths;
            double median = n % 2 == 1 ? left[n / 2] : (left[n / 2 - 1] + left[n / 2]) / 2;

            return new PlanResult(new SpendOptimum(recommended, (double)survivors / n, claimingAges), median);
        }

        // How many screened candidates get a full break-even search besides the first.
        internal const int Finalists = 2;

        // The candidate with the highest midpoint spend for one claiming age. A full break-even search
        // for all 8 candidates would take ~4x today's time, so it's two-stage: the first candidate (Tax-optimized,
        // the 12% line) is searched in full, and its midpoint spend becomes the probe; every other candidate is
        // screened with one run per path at that spend (survivors, then after-tax money left); the top Finalists
        // are searched in full, warm-started from the first candidate's break-evens on the same paths.
        private static ClaimingAgeResult BestCandidate(
            OptimizationInputs inputs, SimulationParameters template, int age, DateOnly start, double monthly,
            IReadOnlyList<(WithdrawalStrategy Order, RothConversionTarget Target)> candidates, double[][] paths,
            CancellationToken cancellationToken)
        {
            ClaimingAgeResult FullSearch((WithdrawalStrategy Order, RothConversionTarget Target) c, double[]? hints, out double[] breakEvens)
            {
                var simulator = new PathSimulator(template, start, monthly, c.Order, c.Target, paths);
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
                    var simulator = new PathSimulator(template, start, monthly, c.Order, c.Target, paths);
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
