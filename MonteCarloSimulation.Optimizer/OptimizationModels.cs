using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // What to optimize. Template carries everything the main simulation needs except the parts being solved for:
    // its Withdrawal, Iterations and Social Security fields are ignored. Its market is the one optimized for: its
    // AssetMix when it has one (the Optimal page), else Mean/StdDev.
    public sealed class OptimizationInputs
    {
        public required SimulationParameters Template { get; init; }
        public required SocialSecurityCurve SocialSecurity { get; init; }
        public int Paths { get; init; } = SpendingOptimizer.DefaultPaths;
    }

    // One Social Security claiming age: the most annual spending (today's dollars) that survives 85%, 82.5% and 80%
    // of the simulated market paths. TotalSocialSecurity is every benefit payment received from the start date to
    // the end of the retirement window, in actual (inflated) dollars. WithdrawalStrategy and RothConversionTarget are
    // the order and conversion target these spends were found with (the best pair when the app chooses).
    public sealed record ClaimingAgeResult(
        int Age,
        DateOnly StartDate,
        double MonthlyBenefit,
        double TotalSocialSecurity,
        double SpendAt85,
        double SpendAtMidpoint,
        double SpendAt80,
        WithdrawalStrategy WithdrawalStrategy,
        RothConversionTarget RothConversionTarget);

    // The recommendation: the claiming age with the highest midpoint (82.5%) spend, the share of paths that actually
    // survive at that spend, and every claiming age for comparison.
    public sealed record SpendOptimum(
        ClaimingAgeResult Recommended,
        double VerifiedSurvivalRate,
        IReadOnlyList<ClaimingAgeResult> ClaimingAges);

    public sealed record BenefitAtAge(int Age, double MonthlyBenefit);

    // What SpendingOptimizer.Optimize reports while it runs. Called from worker threads, possibly at the same time,
    // so it must be thread-safe: ClaimingAgeDone gets the claiming ages finished so far and the total.
    public sealed class OptimizationListener
    {
        public Action<int, int>? ClaimingAgeDone { get; init; }
    }

    public sealed record OptimizationResult(
        SpendOptimum Optimum,
        IReadOnlyList<BenefitAtAge> BenefitByAge,
        int Paths);
}
