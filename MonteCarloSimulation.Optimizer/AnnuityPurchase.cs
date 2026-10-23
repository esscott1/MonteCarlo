using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // An annuity bought on the Retire On date, paying the current rate for its size - payments for life (the horizon)
    // from the Retire On date, as the Scenario runner's SPIA pays, so a plan handed to it runs the same. Level, or with
    // the app's COLA (Annuity.ColaIncrease each January), which starts at Annuity.ColaFirstYearFactor of the level
    // payment. Bought one of two ways:
    //   - Tax Deferred money: a qualified annuity. Tax Deferred falls by the amount, no tax at purchase, and every
    //     payment is ordinary income (no basis).
    //   - After-tax money: a non-qualified annuity, its price the basis that comes back tax-free over its first 312
    //     payments. Brokerage pays first: enough is sold to net its part after an estimate of the tax on the gains the
    //     sale realizes (as if the sale were the first year's only income, so it can share that year's 0% band), and
    //     the sale takes basis and gain in proportion, as Accounts.SellBrokerage does. Roth pays the rest, tax-free:
    //     its basis (contributions) first, then its gains, which are reachable only at 59.5 or older on the Retire On
    //     date (Accounts.EligibleRoth).
    // Pure: it changes a copy of the template.
    public static class AnnuityPurchase
    {
        public const double DefaultStep = 100_000;

        // The Tax Deferred sizes to try: every Step up to the whole Tax Deferred balance; none when the template already
        // has an annuity, the rate isn't positive or the step is
        public static IReadOnlyList<double> Amounts(SimulationParameters template, AnnuityPurchaseOptions options) =>
            Steps(template, options, template.InitialTaxableBalance);

        // The after-tax sizes: every Step up to what Brokerage nets after its sale's estimated tax, plus the Roth reachable
        public static IReadOnlyList<double> AfterTaxAmounts(SimulationParameters template, AnnuityPurchaseOptions options)
        {
            if (template.Annuity is not null) return [];
            var year = RetirementTimeline.Build(template)[0];
            double capacity = year.TaxYear.BrokerageNetCapacity(Brokerage(template), GainFraction(template), 0, 0)
                + Math.Max(0, template.InitialRothBasis) + (year.AgeEligible ? Math.Max(0, template.InitialRothUnrealizedGain) : 0);
            return Steps(template, options, capacity);
        }

        private static IReadOnlyList<double> Steps(SimulationParameters template, AnnuityPurchaseOptions options, double limit)
        {
            if (template.Annuity is not null || !(options.MonthlyPer100k > 0) || !(options.Step > 0)) return [];
            int count = (int)Math.Floor(limit / options.Step + 1e-9);
            return Enumerable.Range(1, Math.Max(0, count)).Select(k => k * options.Step).ToList();
        }

        // What to try: every Tax Deferred size as a level SPIA, each again with a COLA when the options say to, then the
        // same for the after-tax sizes when the options weigh those
        public static IReadOnlyList<AnnuityCandidate> Candidates(SimulationParameters template, AnnuityPurchaseOptions options)
        {
            var candidates = new List<AnnuityCandidate>();
            void Add(IReadOnlyList<double> amounts, AnnuityFunding funding)
            {
                candidates.AddRange(amounts.Select(amount => new AnnuityCandidate(amount, Cola: false, funding)));
                if (options.ConsiderCola) candidates.AddRange(amounts.Select(amount => new AnnuityCandidate(amount, Cola: true, funding)));
            }
            Add(Amounts(template, options), AnnuityFunding.TaxDeferred);
            if (options.ConsiderAfterTax) Add(AfterTaxAmounts(template, options), AnnuityFunding.AfterTax);
            return candidates;
        }

        // The first payment, to the cent
        public static double MonthlyPayment(AnnuityPurchaseOptions options, double amount, bool cola = false) =>
            Math.Round(options.MonthlyPer100k * (cola ? Annuity.ColaFirstYearFactor : 1) * amount / 100_000, 2);

        // What a purchase of `amount` takes from each account (see the class comment)
        public static AnnuitySource Source(SimulationParameters template, double amount, AnnuityFunding funding)
        {
            if (funding == AnnuityFunding.TaxDeferred) return new AnnuitySource(funding, amount, 0, 0, 0, 0, 0);

            var year = RetirementTimeline.Build(template)[0];
            double brokerage = Brokerage(template);
            double gainFraction = GainFraction(template);
            double fromBrokerage = Math.Min(amount, year.TaxYear.BrokerageNetCapacity(brokerage, gainFraction, 0, 0));
            double sale = fromBrokerage > 0 ? Math.Min(brokerage, year.TaxYear.GrossUpBrokerageSale(fromBrokerage, gainFraction, 0, 0)) : 0;
            double rest = amount - fromBrokerage;
            double rothBasis = Math.Min(Math.Max(0, rest), Math.Max(0, template.InitialRothBasis));
            double rothGain = year.AgeEligible ? Math.Min(Math.Max(0, rest - rothBasis), Math.Max(0, template.InitialRothUnrealizedGain)) : 0;
            return new AnnuitySource(
                funding,
                FromTaxDeferred: 0,
                FromBrokerageBasis: sale * (1 - gainFraction),
                FromBrokerageGain: sale * gainFraction,
                BrokerageSaleTax: sale - fromBrokerage,
                FromRothBasis: rothBasis,
                FromRothGain: rothGain);
        }

        public static SimulationParameters Apply(
            SimulationParameters template, AnnuityPurchaseOptions options, double amount, bool cola = false,
            AnnuityFunding funding = AnnuityFunding.TaxDeferred)
        {
            var bought = ParametersCopy.Of(template);
            if (funding == AnnuityFunding.TaxDeferred)
            {
                bought.InitialTaxableBalance = template.InitialTaxableBalance - amount;
            }
            else
            {
                // Floored at 0: an account sold or drawn whole mustn't be left with float dust below it
                var source = Source(template, amount, funding);
                bought.InitialBrokerageBasis = Math.Max(0, template.InitialBrokerageBasis - source.FromBrokerageBasis);
                bought.InitialBrokerageUnrealizedGain = source.FromBrokerageGain > 0
                    ? Math.Max(0, template.InitialBrokerageUnrealizedGain - source.FromBrokerageGain)
                    : template.InitialBrokerageUnrealizedGain;
                bought.InitialRothBasis = Math.Max(0, template.InitialRothBasis - source.FromRothBasis);
                bought.InitialRothUnrealizedGain = source.FromRothGain > 0
                    ? Math.Max(0, template.InitialRothUnrealizedGain - source.FromRothGain)
                    : template.InitialRothUnrealizedGain;
            }
            bought.Annuity = new Annuity
            {
                MonthlyPayment = MonthlyPayment(options, amount, cola),
                FirstPayment = template.RetirementDate,
                Basis = funding == AnnuityFunding.AfterTax ? amount : 0,
                AnnualIncrease = cola ? Annuity.ColaIncrease : 0
            };
            return bought;
        }

        private static double Brokerage(SimulationParameters p) => Math.Max(0, p.InitialBrokerageBasis + p.InitialBrokerageUnrealizedGain);

        // As Accounts.BrokerageGainFraction: the embedded gain's share, clamped so a loss is never a rebate
        private static double GainFraction(SimulationParameters p)
        {
            double brokerage = p.InitialBrokerageBasis + p.InitialBrokerageUnrealizedGain;
            return brokerage > 0 ? Math.Clamp(p.InitialBrokerageUnrealizedGain / brokerage, 0.0, 1.0) : 0;
        }
    }
}
