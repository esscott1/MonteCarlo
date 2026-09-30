namespace MonteCarloSimulation.Core
{
    // How far a year's Roth conversion fills ordinary income (ConversionTargets maps each to its ceiling). Not a
    // user input: the user only allows or forbids conversions (EnableRothConversions); the app picks the target.
    public enum RothConversionTarget
    {
        // No conversions.
        None,
        // Up to the start of the 22% bracket - filling the 12% bracket - or lower when more ordinary income would
        // push this year's realized gains out of the 0% capital gains band (TaxYear.OrdinaryFillCeiling).
        Bracket12,
        // Up to the top of the 22% bracket.
        Bracket22,
        // Up to the top of the 24% bracket.
        Bracket24,
        // The app's default: before running, try each target (with each withdrawal order) on these inputs and use
        // the pair that sustains the spending best (AutomaticStrategy).
        Automatic
    }
}
