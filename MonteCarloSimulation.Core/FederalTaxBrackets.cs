namespace MonteCarloSimulation.Core
{
    public record TaxBracket(double LowerBound, double UpperBound, double Rate);

    public static class FederalTaxBrackets
    {
        // Tax year 2026, single filer. Source: IRS IR-2025-103 / Rev. Proc. 2025-32.
        public static readonly IReadOnlyList<TaxBracket> Single2026 = new List<TaxBracket>
        {
            new(0, 12400, 0.10),
            new(12400, 50400, 0.12),
            new(50400, 105700, 0.22),
            new(105700, 201775, 0.24),
            new(201775, 256225, 0.32),
            new(256225, 640600, 0.35),
            new(640600, double.PositiveInfinity, 0.37)
        };
    }
}
