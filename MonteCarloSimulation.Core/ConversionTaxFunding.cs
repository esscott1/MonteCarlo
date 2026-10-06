namespace MonteCarloSimulation.Core
{
    // Where a Roth conversion's tax comes from (RothConversion.Apply).
    public enum ConversionTaxFunding
    {
        // Only by selling Brokerage; a conversion is scaled down to what Brokerage can pay.
        Brokerage,
        // Always out of the converted amount, so less of it reaches Roth.
        FromConversion,
        // Brokerage while it can pay, the rest out of the conversion.
        BrokerageThenConversion,
        // Like BrokerageThenConversion, but before 59.5 Brokerage only pays from what it holds beyond the spending
        // still to come before the gate opens.
        BridgeAware
    }
}
