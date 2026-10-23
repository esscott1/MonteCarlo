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
        // Annuity purchases to consider (AnnuityPurchase), or null for none. Ignored when the template already has an
        // annuity: the search buys at most one.
        public AnnuityPurchaseOptions? Purchase { get; init; }
    }

    // A SPIA bought on the Retire On date with Tax Deferred money (a qualified annuity: no tax at purchase, every payment
    // ordinary income), in multiples of Step up to the whole Tax Deferred balance. MonthlyPer100k is the current best
    // payout per $100,000 for the household's age then, as of RateAsOf. ConsiderCola also weighs each size with the app's
    // COLA (2% a year, starting 10% lower); ConsiderAfterTax also weighs a non-qualified one bought with Brokerage and
    // then Roth money (AnnuityPurchase).
    public sealed record AnnuityPurchaseOptions(
        double MonthlyPer100k, string RateAsOf, double Step = AnnuityPurchase.DefaultStep, bool ConsiderCola = false, bool ConsiderAfterTax = false);

    // Where a purchase's money comes from: Tax Deferred (a qualified annuity) or after-tax money, Brokerage then Roth
    // (a non-qualified one)
    public enum AnnuityFunding { TaxDeferred, AfterTax }

    // One purchase to weigh: a size, level or with the COLA, and how it's paid for
    public sealed record AnnuityCandidate(double Amount, bool Cola, AnnuityFunding Funding = AnnuityFunding.TaxDeferred);

    // What a purchase takes from each of the household's balances (as SimulationParameters holds them): BrokerageSaleTax
    // is the estimated tax on the gains the Brokerage sale realizes, part of what it takes from Brokerage
    public sealed record AnnuitySource(
        AnnuityFunding Funding,
        double FromTaxDeferred,
        double FromBrokerageBasis,
        double FromBrokerageGain,
        double BrokerageSaleTax,
        double FromRothBasis,
        double FromRothGain);

    // What the search decided. Amount 0 means no purchase raised the spend (and MonthlyPayment is 0). SpendWithout and
    // SpendWith are the recommended midpoint (82.5%) annual spends without and with the purchase (equal with none);
    // MoneyLeftWithout and MoneyLeftWith the median after-tax money left at the end of the window, over every path,
    // each at its own recommended spend (a failed path counts as 0). AmountsConsidered is how many purchases were tried
    // (each size once, or twice when COLAs were weighed too, for each way of paying). MonthlyPayment is the first payment;
    // AnnualIncrease the bought annuity's COLA (Annuity.ColaIncrease), 0 for a level one. Source says what the purchase
    // took from each balance (null with none), so a page can show it and add it back.
    public sealed record AnnuityPurchaseDecision(
        double Amount,
        double MonthlyPayment,
        double MonthlyPer100k,
        string RateAsOf,
        double SpendWithout,
        double SpendWith,
        double MoneyLeftWithout,
        double MoneyLeftWith,
        int AmountsConsidered,
        double AnnualIncrease = 0,
        AnnuitySource? Source = null);

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
    // survive at that spend, and every claiming age for comparison. When purchases were considered, Purchase says what
    // was decided, and the rest describes the plan with that purchase (or without one, when none helped).
    public sealed record SpendOptimum(
        ClaimingAgeResult Recommended,
        double VerifiedSurvivalRate,
        IReadOnlyList<ClaimingAgeResult> ClaimingAges,
        AnnuityPurchaseDecision? Purchase = null);

    public sealed record BenefitAtAge(int Age, double MonthlyBenefit);

    // What SpendingOptimizer.Optimize reports while it runs. Called from worker threads, possibly at the same time,
    // so it must be thread-safe: StepDone gets the steps finished so far and the total. A step is one claiming age's
    // search (9 per plan searched: the plan without a purchase, then each finalist purchase), or screening the purchase
    // sizes (one step).
    public sealed class OptimizationListener
    {
        public Action<int, int>? StepDone { get; init; }
    }

    public sealed record OptimizationResult(
        SpendOptimum Optimum,
        IReadOnlyList<BenefitAtAge> BenefitByAge,
        int Paths);
}
