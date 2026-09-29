namespace MonteCarloSimulation.Core
{
    // Ordered draw: Tax Deferred up to the top of the 12% bracket, then Brokerage, then more Tax Deferred
    // into the 22%+ brackets, then Roth as last resort. The 12% ceiling is a preference, not a hard cap -
    // otherwise a run fails with Tax Deferred money still on the books once Brokerage and Roth run dry.
    // Each draw is capped at what that bucket can net after tax, so no bucket goes negative; anything
    // still unmet is a shortfall.
    internal sealed class TaxOptimizedWithdrawalStrategy : IWithdrawalStrategy
    {
        public WithdrawalPlan Plan(in WithdrawalContext c)
        {
            double remaining = c.Need;

            double maxTaxableGross = Math.Min(Math.Max(0, c.TaxYear.Bracket22CeilingGross - c.SsTaxable), Math.Max(0, c.EligibleTaxable));
            double maxTaxableNet = maxTaxableGross - c.TaxYear.IncrementalOrdinaryTax(c.SsTaxable, maxTaxableGross);
            double netTaxable = Math.Min(remaining, maxTaxableNet);
            // Clamp: re-grossing a net derived from the full cap can overshoot it by float rounding,
            // which would leave the bucket at -1e-10 and falsely trip the negative-balance failure.
            double grossTaxable = Math.Min(maxTaxableGross, c.TaxYear.GrossUpOrdinary(netTaxable, c.SsTaxable));
            remaining -= netTaxable;

            double brokerageNetCapacity = TaxAssumptions.BrokerageNetCapacity(c.Brokerage, c.GainFraction);
            double netBrokerage = Math.Min(remaining, brokerageNetCapacity);
            double grossBrokerage = Math.Min(Math.Max(0, c.Brokerage), TaxAssumptions.GrossUpLtcg(netBrokerage, c.GainFraction));
            remaining -= netBrokerage;

            if (remaining > 0 && c.EligibleTaxable > 0)
            {
                double fullTaxableGross = c.EligibleTaxable;
                double fullTaxableNet = fullTaxableGross - c.TaxYear.IncrementalOrdinaryTax(c.SsTaxable, fullTaxableGross);
                double totalTaxableNet = Math.Min(netTaxable + remaining, fullTaxableNet);
                remaining -= totalTaxableNet - netTaxable;
                netTaxable = totalTaxableNet;
                grossTaxable = Math.Min(fullTaxableGross, c.TaxYear.GrossUpOrdinary(netTaxable, c.SsTaxable));
            }

            double roth = Math.Min(remaining, Math.Max(0, c.EligibleRoth));
            remaining -= roth;

            return new WithdrawalPlan(netTaxable, grossTaxable, netBrokerage, grossBrokerage, roth, IsShortfall: remaining > 0.01);
        }
    }
}
