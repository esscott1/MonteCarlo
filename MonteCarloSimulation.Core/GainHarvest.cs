namespace MonteCarloSimulation.Core
{
    // Tax-gain harvesting: when the year's income leaves room in the 0% long-term capital gains band, sell and
    // immediately rebuy enough Brokerage holdings to realize that much embedded gain at 0%. No cash moves and no
    // tax is due - the only effect is a higher cost basis, which lowers the taxable share of future sales.
    // (The wash-sale rule applies only to losses, so the immediate rebuy is allowed.)
    internal static class GainHarvest
    {
        // Returns the gains harvested.
        public static double Apply(Accounts accounts, TaxYear taxYear, double ordinaryIncome, double realizedGains)
        {
            double room = taxYear.ZeroRateGainRoom(ordinaryIncome, realizedGains);
            double embeddedGain = Math.Max(0, accounts.Brokerage - accounts.BrokerageBasis);
            double harvested = Math.Min(room, embeddedGain);
            if (harvested <= 0) return 0;

            accounts.StepUpBrokerageBasis(harvested);
            return harvested;
        }
    }
}
