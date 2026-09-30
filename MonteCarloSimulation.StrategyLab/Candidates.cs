using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // A withdrawal order the lab compares. W1 and W2 are the app's own strategies. Definition is the plain-language
    // rule shown on the Model Info page.
    internal sealed record WithdrawalOrder(string Code, string Name, string Definition, IWithdrawalStrategy Strategy);

    // A Roth conversion policy. C0 converts nothing; C1 is the app's own ceiling.
    internal sealed record ConversionPolicy(string Code, string Name, string Definition, ConversionCeiling? Ceiling)
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
        public static readonly WithdrawalOrder ProRata = new("W1", "Pro-rata",
            "Each year's spending comes from every accessible account in proportion to its balance (Tax Deferred and Roth gains only from 59½).",
            new ProRataWithdrawalStrategy());
        public static readonly WithdrawalOrder TaxOptimized = new("W2", "Tax-optimized (gains first)",
            "The app's order. Brokerage while its gains are taxed at 0%, then Tax Deferred up to the start of the 22% bracket (or less, to keep those gains at 0%), " +
            "then more Brokerage at 15/20%, then more Tax Deferred, then Roth. Any 0% capital gains room left over is harvested.",
            new TaxOptimizedWithdrawalStrategy());
        public static readonly WithdrawalOrder TaxDeferredToBracketFirst = new("W3", "Tax Deferred to 22% line first (previous)",
            "The earlier tax-optimized order: Tax Deferred up to the start of the 22% bracket, then Brokerage, then more Tax Deferred, then Roth.",
            new TaxDeferredToBracketFirstStrategy());
        public static readonly WithdrawalOrder BrokerageFirst = new("W4", "Brokerage > Tax Deferred > Roth",
            "The conventional rule of thumb: Brokerage until it's empty, then Tax Deferred, then Roth.",
            new BrokerageFirstStrategy());
        public static readonly WithdrawalOrder TaxDeferredFirst = new("W5", "Tax Deferred > Brokerage > Roth",
            "Drain Tax Deferred first: Tax Deferred until it's empty, then Brokerage, then Roth.",
            new TaxDeferredFirstStrategy());
        public static readonly WithdrawalOrder Greedy = new("W6", "Greedy lowest marginal tax",
            "Each chunk of spending ($1,000, or 1/25 of the year's need if larger) comes from whichever of Tax Deferred and Brokerage adds the least tax this year; Roth last.",
            new GreedyMarginalStrategy());
        public static readonly WithdrawalOrder TaxOptimizedNoHarvest = new("W2nh", "Tax-optimized, no 0% harvesting",
            "W2 without the 0% capital gains harvesting, to isolate what harvesting adds.",
            new TaxOptimizedWithoutHarvestStrategy());

        public static readonly ConversionPolicy NoConversions = new("C0", "No conversions",
            "No Roth conversions.", null);
        public static readonly ConversionPolicy AppDefault = new("C1", "To 22% line, protecting 0% gains (app)",
            "The app's policy. Up to the start of the 22% bracket, or lower when more ordinary income would push the gains already realized this year out of the 0% capital gains band.",
            RothConversion.DefaultCeiling);
        public static readonly ConversionPolicy To22Line = new("C2", "To 22% line, ignoring 0% gains",
            "Up to the start of the 22% bracket, even if that moves this year's 0% gains to 15%.",
            (t, _, _) => t.Bracket22CeilingGross);
        public static readonly ConversionPolicy Top22 = new("C3", "To top of 22% bracket",
            "Up to the top of the 22% bracket.",
            (t, _, _) => BracketStart(t, 0.24));
        public static readonly ConversionPolicy Top24 = new("C4", "To top of 24% bracket",
            "Up to the top of the 24% bracket.",
            (t, _, _) => BracketStart(t, 0.32));

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

        // The definitions tables. Conversion lines are evaluated for a 2026 single filer with the lab's $16,000
        // deduction and nothing else counted yet; C1's alternative line applies once gains have been realized.
        public static IReadOnlyList<StrategyDefinition> Definitions()
        {
            var taxYear2026 = new TaxYear(ScenarioGenerator.StandardDeduction, 1.0, FederalTaxBrackets.Single2026, FederalTaxBrackets.CapitalGainsSingle2026);
            var orders = Orders.Append(TaxOptimizedNoHarvest)
                .Select(o => new StrategyDefinition(o.Code, "order", o.Name, o.Definition, null, null));
            var policies = Policies.Select(p => new StrategyDefinition(p.Code, "conversion", p.Name, p.Definition,
                p.Ceiling?.Invoke(taxYear2026, 0, 0),
                p == AppDefault ? taxYear2026.ZeroRateCeilingGross : null));
            return orders.Concat(policies).ToList();
        }

        // Gross ordinary income where the first bracket at `rate` or higher starts.
        private static double BracketStart(TaxYear t, double rate) =>
            t.StandardDeduction + t.Brackets.First(b => b.Rate >= rate).LowerBound * t.InflationFactor;
    }

    // One row of the Model Info page's definitions tables. For a conversion policy, Line2026 is the gross ordinary
    // income it converts up to in 2026; ZeroRateAlternative (C1 only) is the 0% capital gains ceiling, from which
    // the year's realized gains are subtracted when that's lower.
    internal sealed record StrategyDefinition(string Code, string Kind, string Name, string Definition, double? Line2026, double? ZeroRateAlternative);
}
