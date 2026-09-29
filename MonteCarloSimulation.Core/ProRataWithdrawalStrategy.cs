namespace MonteCarloSimulation.Core
{
    // Splits the need across the eligible buckets in proportion to their eligible balances. Once the age
    // gate opens this reduces to exactly the unrestricted 3-way split.
    internal sealed class ProRataWithdrawalStrategy : IWithdrawalStrategy
    {
        public WithdrawalPlan Plan(in WithdrawalContext c)
        {
            double eligibleTotal = c.EligibleTaxable + c.Brokerage + c.EligibleRoth;
            double taxableProportion = eligibleTotal > 0 ? c.EligibleTaxable / eligibleTotal : 0;
            double brokerageProportion = eligibleTotal > 0 ? c.Brokerage / eligibleTotal : 0;
            double rothProportion = eligibleTotal > 0 ? c.EligibleRoth / eligibleTotal : 0;

            // If accessible money can't cover the desired withdrawal - even though locked funds
            // still exist - cap what's actually withdrawn and treat the year as a failure.
            bool isShortfall = eligibleTotal < c.Need;
            double baseWithdrawal = Math.Min(c.Need, eligibleTotal);

            // Gross up the Tax Deferred share (to net the correct after-tax amount), stacked on top of the
            // taxable share of Social Security in the inflation-scaled brackets
            double netTaxable = baseWithdrawal * taxableProportion;
            double grossTaxable = c.TaxYear.GrossUpOrdinary(netTaxable, c.SsTaxable);

            double netBrokerage = baseWithdrawal * brokerageProportion;
            double grossBrokerage = TaxAssumptions.GrossUpLtcg(netBrokerage, c.GainFraction);

            double roth = baseWithdrawal * rothProportion;

            return new WithdrawalPlan(netTaxable, grossTaxable, netBrokerage, grossBrokerage, roth, isShortfall);
        }
    }
}
