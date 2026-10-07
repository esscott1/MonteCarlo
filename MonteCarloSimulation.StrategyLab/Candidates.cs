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

    // A rule for where a conversion's tax comes from (Core's ConversionTaxFunding).
    internal sealed record FundingRule(string Code, string Name, string Definition, ConversionTaxFunding Value);

    // One (order, policy[, funding]) combination evaluated on every scenario. A null Funding uses the app's
    // current default; the code names the funding rule only when it's set explicitly.
    internal sealed record Combination(WithdrawalOrder Order, ConversionPolicy Policy, FundingRule? Funding = null)
    {
        public string Code => Funding is null ? $"{Order.Code}+{Policy.Code}" : $"{Order.Code}+{Policy.Code}+{Funding.Code}";
    }

    // A set of combinations run together. The baseline comes first: every other combination is compared with it.
    // ProRataCode is the Pro-rata combination the report's case study and subsets compare against the baseline.
    internal sealed record CandidateSet(string Name, IReadOnlyList<Combination> All, string ProRataCode)
    {
        public Combination Baseline => All[0];
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

        public static readonly FundingRule FundingBrokerage = new("F0", "Brokerage only",
            "The conversion's tax is paid only by selling Brokerage; a conversion is scaled down to what Brokerage can pay.",
            ConversionTaxFunding.Brokerage);
        public static readonly FundingRule FundingFromConversion = new("F1", "Out of the conversion",
            "The tax always comes out of the converted amount, so less of it reaches Roth.",
            ConversionTaxFunding.FromConversion);
        public static readonly FundingRule FundingBrokerageThenConversion = new("F2", "Brokerage, then the conversion",
            "Brokerage pays while it can; the rest comes out of the converted amount, so conversions never stall.",
            ConversionTaxFunding.BrokerageThenConversion);
        public static readonly FundingRule FundingBridgeAware = new("F3", "Bridge-aware",
            "Like F2, but before 59½ Brokerage pays only from what it holds beyond the spending still to come before 59½.",
            ConversionTaxFunding.BridgeAware);

        public static readonly IReadOnlyList<FundingRule> Fundings = [FundingBrokerage, FundingFromConversion, FundingBrokerageThenConversion, FundingBridgeAware];

        public static readonly ConversionPolicy NoConversions = new("C0", "No conversions",
            "No Roth conversions.", null);
        public static readonly ConversionPolicy AppDefault = new("C1", "12% fill: to the 22% line, protecting 0% gains",
            "Fills the 12% bracket: up to the start of the 22% bracket, or lower when more ordinary income would push the gains already realized this year out of the 0% capital gains band.",
            RothConversion.DefaultCeiling);
        public static readonly ConversionPolicy To22Line = new("C2", "To 22% line, ignoring 0% gains",
            "Up to the start of the 22% bracket, even if that moves this year's 0% gains to 15%.",
            (t, _, _) => t.Bracket22CeilingGross);
        public static readonly ConversionPolicy Top22 = new("C3", "To top of 22% bracket",
            "Up to the top of the 22% bracket.",
            ConversionTargets.TopOf22);
        public static readonly ConversionPolicy Top24 = new("C4", "To top of 24% bracket",
            "Up to the top of the 24% bracket.",
            ConversionTargets.TopOf24);
        // Lab-only unless it earns a place among the app's targets: up to the top of the 22% bracket, but stopping where
        // the year's ordinary income plus gains realized so far would cross the first Medicare IRMAA tier.
        public static readonly ConversionPolicy BelowIrmaa = new("C5", "To top of 22%, below the first IRMAA tier",
            "Up to the top of the 22% bracket, but stopping where the year's income (ordinary plus gains realized so far) would cross the first Medicare IRMAA tier.",
            (t, _, gains) => Math.Min(ConversionTargets.BracketStart(t, 0.24),
                t.IrmaaTiers[0].MagiAbove * t.InflationFactor - gains));

        public static readonly IReadOnlyList<WithdrawalOrder> Orders =
            [ProRata, TaxOptimized, TaxDeferredToBracketFirst, BrokerageFirst, TaxDeferredFirst, Greedy];

        public static readonly IReadOnlyList<ConversionPolicy> Policies = [NoConversions, AppDefault, To22Line, Top22, Top24, BelowIrmaa];

        // The app's candidate targets, as lab policies (AutomaticStrategy.Targets).
        public static readonly IReadOnlyDictionary<RothConversionTarget, ConversionPolicy> AppTargets = new Dictionary<RothConversionTarget, ConversionPolicy>
        {
            [RothConversionTarget.None] = NoConversions,
            [RothConversionTarget.Bracket12] = AppDefault,
            [RothConversionTarget.Bracket22] = Top22,
            [RothConversionTarget.Bracket24] = Top24,
        };

        public static readonly IReadOnlyDictionary<WithdrawalStrategy, WithdrawalOrder> AppOrders = new Dictionary<WithdrawalStrategy, WithdrawalOrder>
        {
            [WithdrawalStrategy.TaxOptimized] = TaxOptimized,
            [WithdrawalStrategy.ProRata] = ProRata,
        };

        // Today's default (W2+C1) comes first: it's the baseline every other combination is measured against.
        public static readonly Combination Baseline = new(TaxOptimized, AppDefault);

        public static readonly IReadOnlyList<Combination> All =
            new[] { Baseline }
                .Concat(Orders.SelectMany(o => Policies.Select(p => new Combination(o, p))).Where(c => c.Code != Baseline.Code))
                .Append(new Combination(TaxOptimizedNoHarvest, AppDefault))
                .ToList();

        // The main grid: every order x conversion policy under the app's conversion-tax funding.
        public static readonly CandidateSet Main = new("main", All, "W1+C1");

        // Where conversion tax comes from: Pro-rata and Tax-optimized x the converting policies x every funding rule,
        // against today's W2+C1 with Brokerage-only funding (plus both orders without conversions, where funding
        // doesn't matter).
        public static readonly CandidateSet Funding = new("funding",
            new[] { new Combination(TaxOptimized, AppDefault, FundingBrokerage) }
                .Concat(new[] { ProRata, TaxOptimized }.SelectMany(o => new[] { AppDefault, To22Line, Top22, Top24 }
                    .SelectMany(p => Fundings.Select(f => new Combination(o, p, f)))))
                .Where((c, i) => i == 0 || c.Code != "W2+C1+F0")
                .Concat([new Combination(ProRata, NoConversions), new Combination(TaxOptimized, NoConversions)])
                .ToList(),
            "W1+C1+F0");

        public static CandidateSet SetNamed(string name) =>
            new[] { Main, Funding }.SingleOrDefault(s => s.Name == name) ?? throw new ArgumentException($"Unknown set '{name}' (main, funding)");

        // The definitions tables. Conversion lines are evaluated for a 2026 single filer with the lab's $16,000
        // deduction and nothing else counted yet; C1's alternative line applies once gains have been realized.
        public static IReadOnlyList<StrategyDefinition> Definitions()
        {
            var taxYear2026 = TaxYear.For(FilingStatus.Single, ScenarioGenerator.StandardDeduction, 1.0);
            var orders = Orders.Append(TaxOptimizedNoHarvest)
                .Select(o => new StrategyDefinition(o.Code, "order", o.Name, o.Definition, null, null));
            var policies = Policies.Select(p => new StrategyDefinition(p.Code, "conversion", p.Name, p.Definition,
                p.Ceiling?.Invoke(taxYear2026, 0, 0),
                p == AppDefault ? taxYear2026.ZeroRateCeilingGross : null));
            var appFunding = new SimulationParameters { ScenarioDescription = "" }.ConversionTaxFunding;
            var fundings = Fundings.Select(f => new StrategyDefinition(f.Code, "funding",
                f.Value == appFunding ? $"{f.Name} (app)" : f.Name, f.Definition, null, null));
            return orders.Concat(policies).Concat(fundings).ToList();
        }
    }

    // One row of the Model Info page's definitions tables. For a conversion policy, Line2026 is the gross ordinary
    // income it converts up to in 2026; ZeroRateAlternative (C1 only) is the 0% capital gains ceiling, from which
    // the year's realized gains are subtracted when that's lower.
    internal sealed record StrategyDefinition(string Code, string Kind, string Name, string Definition, double? Line2026, double? ZeroRateAlternative);
}
