namespace MonteCarloSimulation.Core
{
    // How the household files its federal return. It picks the year's bracket tables and Medicare IRMAA tiers
    // (FederalTaxBrackets.For). Married filing jointly is modelled as two people the same age with one combined
    // Social Security benefit: both pay Medicare's surcharge.
    public enum FilingStatus
    {
        Single,
        MarriedJoint
    }
}
