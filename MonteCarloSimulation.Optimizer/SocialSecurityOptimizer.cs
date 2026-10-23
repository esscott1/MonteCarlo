using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // The Scenario runner's Optimize Social Security: with everything else held - the spend, the balances, any owned
    // annuity - which Social Security start lets the most simulated markets survive? Each birthday from 62 to 70 is
    // tried, at the curve's benefit for that age, as the Optimal page tries them, on the same seeded paths (path i =
    // new Random(i)), each path run once at the fixed spend. At each age the order and conversion line are the best of
    // the engine's own candidates (AutomaticStrategy.Candidates) by the same rule: the most survivors, then the most
    // after-tax money left. The best age wins by that rule too, ties going to the earlier age. The template's own claim
    // is measured on the same paths, so the two compare like with like.
    public static class SocialSecurityOptimizer
    {
        public static SocialSecurityChoice BestClaim(
            SimulationParameters template, SocialSecurityCurve curve, double annualSpend, int paths,
            CancellationToken cancellationToken = default)
        {
            var market = PathSimulator.SeededPaths(template, paths);
            var candidates = AutomaticStrategy.Candidates(template);
            var ages = SpendingOptimizer.ClaimingAges;
            var byAge = new ClaimSurvival[ages.Count];

            // Capped at one worker per core, as the Optimizer's own search is, so the web server keeps threads to answer with
            Parallel.For(0, ages.Count,
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
                i => byAge[i] = Survival(template, template.Birthdate.AddYears(ages[i]), curve.MonthlyBenefitAtAge(ages[i]),
                    annualSpend, candidates, market, paths, cancellationToken));

            var best = byAge.OrderByDescending(c => c.Survivors).ThenByDescending(c => c.AfterTaxLeft).ThenBy(c => c.StartDate).First();
            var current = Survival(template, template.SocialSecurityStartDate, template.SocialSecurityMonthlyAmount,
                annualSpend, candidates, market, paths, cancellationToken);
            return new SocialSecurityChoice(best, current, byAge, paths);
        }

        // One start date and benefit at the fixed spend: its best candidate's survivors and average after-tax money left
        private static ClaimSurvival Survival(
            SimulationParameters template, DateOnly start, double monthly, double annualSpend,
            IReadOnlyList<(WithdrawalStrategy Order, RothConversionTarget Target)> candidates, double[][] market, int paths,
            CancellationToken cancellationToken)
        {
            ClaimSurvival? best = null;
            foreach (var (order, target) in candidates)
            {
                var simulator = new PathSimulator(template, start, monthly, order, target, market);
                int survivors = 0;
                double left = 0;
                for (int path = 0; path < paths; path++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (survived, afterTax) = simulator.Probe(annualSpend, path);
                    if (!survived) continue;
                    survivors++;
                    left += afterTax;
                }
                var result = new ClaimSurvival(
                    AgeOn(template.Birthdate, start), start, monthly, survivors, (double)survivors / paths, left / paths, order, target);
                if (best is null || survivors > best.Survivors || (survivors == best.Survivors && result.AfterTaxLeft > best.AfterTaxLeft))
                    best = result;
            }
            return best!;
        }

        // Whole years on that date
        private static int AgeOn(DateOnly birthdate, DateOnly date) =>
            date.Year - birthdate.Year - (date < birthdate.AddYears(date.Year - birthdate.Year) ? 1 : 0);
    }

    // How one Social Security start fares at the fixed spend: the share of paths that survive and the after-tax money
    // left on average over every path (a failed path as 0), with the order and conversion line that did best there
    public sealed record ClaimSurvival(
        int Age,
        DateOnly StartDate,
        double MonthlyBenefit,
        int Survivors,
        double SurvivalRate,
        double AfterTaxLeft,
        WithdrawalStrategy WithdrawalStrategy,
        RothConversionTarget RothConversionTarget);

    // The best start (Best), the template's own (Current), every birthday 62-70 (ByAge), on Paths simulated markets.
    // Improves: the best start survives more markets than the current one.
    public sealed record SocialSecurityChoice(ClaimSurvival Best, ClaimSurvival Current, IReadOnlyList<ClaimSurvival> ByAge, int Paths)
    {
        public bool Improves => Best.Survivors > Current.Survivors;
    }
}
