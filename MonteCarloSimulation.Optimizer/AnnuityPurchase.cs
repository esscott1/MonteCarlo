using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Optimizer
{
    // An annuity bought on the Retire On date with Tax Deferred money: the plan starts with that much less in Tax
    // Deferred and a qualified SPIA paying the current rate for its size - payments for life (the horizon) from the
    // Retire On date, as the Scenario runner's SPIA pays, so a plan handed to it runs the same; every payment ordinary
    // income (no basis). Level, or with the app's COLA (Annuity.ColaIncrease each January), which starts at
    // Annuity.ColaFirstYearFactor of the level payment. Pure: it changes a copy of the template.
    public static class AnnuityPurchase
    {
        public const double DefaultStep = 100_000;

        // The sizes to try: every Step up to the whole Tax Deferred balance; none when the template already has an
        // annuity, the rate isn't positive or the step is
        public static IReadOnlyList<double> Amounts(SimulationParameters template, AnnuityPurchaseOptions options)
        {
            if (template.Annuity is not null || !(options.MonthlyPer100k > 0) || !(options.Step > 0)) return [];
            int count = (int)Math.Floor(template.InitialTaxableBalance / options.Step + 1e-9);
            return Enumerable.Range(1, Math.Max(0, count)).Select(k => k * options.Step).ToList();
        }

        // What to try: every size as a level SPIA, and each again with a COLA when the options say to
        public static IReadOnlyList<AnnuityCandidate> Candidates(SimulationParameters template, AnnuityPurchaseOptions options)
        {
            var amounts = Amounts(template, options);
            var level = amounts.Select(amount => new AnnuityCandidate(amount, Cola: false));
            return (options.ConsiderCola ? level.Concat(amounts.Select(amount => new AnnuityCandidate(amount, Cola: true))) : level).ToList();
        }

        // The first payment, to the cent
        public static double MonthlyPayment(AnnuityPurchaseOptions options, double amount, bool cola = false) =>
            Math.Round(options.MonthlyPer100k * (cola ? Annuity.ColaFirstYearFactor : 1) * amount / 100_000, 2);

        public static SimulationParameters Apply(SimulationParameters template, AnnuityPurchaseOptions options, double amount, bool cola = false)
        {
            var bought = ParametersCopy.Of(template);
            bought.InitialTaxableBalance = template.InitialTaxableBalance - amount;
            bought.Annuity = new Annuity
            {
                MonthlyPayment = MonthlyPayment(options, amount, cola),
                FirstPayment = template.RetirementDate,
                Basis = 0,
                AnnualIncrease = cola ? Annuity.ColaIncrease : 0
            };
            return bought;
        }
    }
}
