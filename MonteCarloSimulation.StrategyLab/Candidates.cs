using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // A withdrawal order the lab compares. W1 and W2 are the app's own strategies.
    internal sealed record WithdrawalOrder(string Code, string Name, IWithdrawalStrategy Strategy);

    // A Roth conversion policy. C0 converts nothing; C1 is the app's own ceiling.
    internal sealed record ConversionPolicy(string Code, string Name, ConversionCeiling? Ceiling)
    {
        public bool Converts => Ceiling is not null;
    }

    // One (order, policy) pair evaluated on every scenario.
    internal sealed record Combination(WithdrawalOrder Order, ConversionPolicy Policy)
    {
        public string Code => $"{Order.Code}+{Policy.Code}";
    }

    internal static class Candidates
    {
        public static readonly WithdrawalOrder ProRata = new("W1", "Pro-rata", new ProRataWithdrawalStrategy());
        public static readonly WithdrawalOrder TaxOptimized = new("W2", "Tax-optimized (gains first)", new TaxOptimizedWithdrawalStrategy());
        public static readonly WithdrawalOrder TaxDeferredToBracketFirst = new("W3", "Tax Deferred to 22% line first (previous)", new TaxDeferredToBracketFirstStrategy());
        public static readonly WithdrawalOrder BrokerageFirst = new("W4", "Brokerage > Tax Deferred > Roth", new BrokerageFirstStrategy());
        public static readonly WithdrawalOrder TaxDeferredFirst = new("W5", "Tax Deferred > Brokerage > Roth", new TaxDeferredFirstStrategy());
        public static readonly WithdrawalOrder Greedy = new("W6", "Greedy lowest marginal tax", new GreedyMarginalStrategy());
        public static readonly WithdrawalOrder TaxOptimizedNoHarvest = new("W2nh", "Tax-optimized, no 0% harvesting", new TaxOptimizedWithoutHarvestStrategy());

        public static readonly ConversionPolicy NoConversions = new("C0", "No conversions", null);
        public static readonly ConversionPolicy AppDefault = new("C1", "To 22% line, protecting 0% gains (app)", RothConversion.DefaultCeiling);
        public static readonly ConversionPolicy To22Line = new("C2", "To 22% line, ignoring 0% gains", (t, _, _) => t.Bracket22CeilingGross);
        public static readonly ConversionPolicy Top22 = new("C3", "To top of 22% bracket", (t, _, _) => BracketStart(t, 0.24));
        public static readonly ConversionPolicy Top24 = new("C4", "To top of 24% bracket", (t, _, _) => BracketStart(t, 0.32));

        public static readonly IReadOnlyList<WithdrawalOrder> Orders =
            [ProRata, TaxOptimized, TaxDeferredToBracketFirst, BrokerageFirst, TaxDeferredFirst, Greedy];

        public static readonly IReadOnlyList<ConversionPolicy> Policies = [NoConversions, AppDefault, To22Line, Top22, Top24];

        // Today's default (W2+C1) comes first: it's the baseline every other combination is measured against.
        public static readonly Combination Baseline = new(TaxOptimized, AppDefault);

        public static readonly IReadOnlyList<Combination> All =
            new[] { Baseline }
                .Concat(Orders.SelectMany(o => Policies.Select(p => new Combination(o, p))).Where(c => c.Code != Baseline.Code))
                .Append(new Combination(TaxOptimizedNoHarvest, AppDefault))
                .ToList();

        // Gross ordinary income where the first bracket at `rate` or higher starts.
        private static double BracketStart(TaxYear t, double rate) =>
            t.StandardDeduction + t.Brackets.First(b => b.Rate >= rate).LowerBound * t.InflationFactor;
    }
}
