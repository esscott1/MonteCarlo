using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // What to optimize. Template carries everything the main simulation needs except the parts being solved for:
    // its Withdrawal, Iterations, Mean/StdDev, ScenarioDescription and Social Security fields are ignored.
    public sealed class OptimizationInputs
    {
        public required SimulationParameters Template { get; init; }
        public required SocialSecurityCurve SocialSecurity { get; init; }
        public IReadOnlyList<InvestmentScenario> Scenarios { get; init; } = InvestmentScenarios.All;
        public int Paths { get; init; } = SpendingOptimizer.DefaultPaths;
    }

    // One Social Security claiming age for one investment scenario: the most annual spending (today's dollars)
    // that survives 85%, 82.5% and 80% of the simulated market paths.
    public sealed record ClaimingAgeResult(
        int Age,
        DateOnly StartDate,
        double MonthlyBenefit,
        double SpendAt85,
        double SpendAtMidpoint,
        double SpendAt80);

    // The recommendation for one investment scenario: the claiming age with the highest midpoint (82.5%) spend,
    // the share of paths that actually survive at that spend, and every claiming age for comparison.
    public sealed record ScenarioOptimum(
        int ScenarioId,
        string Description,
        ClaimingAgeResult Recommended,
        double VerifiedSurvivalRate,
        IReadOnlyList<ClaimingAgeResult> ClaimingAges);

    public sealed record BenefitAtAge(int Age, double MonthlyBenefit);

    public sealed record OptimizationResult(
        IReadOnlyList<ScenarioOptimum> Scenarios,
        IReadOnlyList<BenefitAtAge> BenefitByAge,
        int Paths);
}
