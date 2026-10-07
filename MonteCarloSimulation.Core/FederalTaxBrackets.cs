namespace MonteCarloSimulation.Core
{
    public record TaxBracket(double LowerBound, double UpperBound, double Rate);

    // One Medicare IRMAA tier: once MAGI is above MagiAbove, this much is added to each month's Medicare premiums
    // (Part B plus Part D surcharges).
    public record IrmaaTier(double MagiAbove, double MonthlySurcharge);

    // One filing status's tables: ordinary brackets, long-term gains brackets, Medicare IRMAA tiers, and how many
    // people on Medicare pay each tier's surcharge.
    public sealed record FilingTables(
        IReadOnlyList<TaxBracket> Ordinary,
        IReadOnlyList<TaxBracket> CapitalGains,
        IReadOnlyList<IrmaaTier> MedicareIrmaa,
        int MedicarePeople);

    public static class FederalTaxBrackets
    {
        // The tax year these tables are for - also the "today's dollars" year the model inflates inputs from.
        public const int Year = 2026;

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

        // Long-term capital gains, tax year 2026, single filer. Source: IRS Rev. Proc. 2025-32 §3.03.
        // Bounds are total taxable income: gains stack on top of ordinary taxable income.
        public static readonly IReadOnlyList<TaxBracket> CapitalGainsSingle2026 = new List<TaxBracket>
        {
            new(0, 49450, 0.00),
            new(49450, 545500, 0.15),
            new(545500, double.PositiveInfinity, 0.20)
        };

        // Medicare IRMAA, 2026, single filer: the Part B + Part D monthly surcharges by MAGI from two years
        // earlier (Part B +81.20/+202.90/+324.60/+446.30/+487.00, Part D +14.50/+37.50/+60.40/+83.30/+91.00).
        // Source: CMS 2026 premiums (reported by Kiplinger, irmaagroup.com). The top tier starts at 500,000
        // inclusive; every other tier at "more than" its threshold - the difference doesn't matter in a
        // continuous model.
        public static readonly IReadOnlyList<IrmaaTier> MedicareIrmaaSingle2026 = new List<IrmaaTier>
        {
            new(109000, 81.20 + 14.50),
            new(137000, 202.90 + 37.50),
            new(171000, 324.60 + 60.40),
            new(205000, 446.30 + 83.30),
            new(500000, 487.00 + 91.00)
        };

        // Tax year 2026, married filing jointly. Source: IRS IR-2025-103 / Rev. Proc. 2025-32.
        public static readonly IReadOnlyList<TaxBracket> MarriedJoint2026 = new List<TaxBracket>
        {
            new(0, 24800, 0.10),
            new(24800, 100800, 0.12),
            new(100800, 211400, 0.22),
            new(211400, 403550, 0.24),
            new(403550, 512450, 0.32),
            new(512450, 768700, 0.35),
            new(768700, double.PositiveInfinity, 0.37)
        };

        // Long-term capital gains, tax year 2026, married filing jointly. Source: IRS Rev. Proc. 2025-32 §3.03.
        public static readonly IReadOnlyList<TaxBracket> CapitalGainsMarriedJoint2026 = new List<TaxBracket>
        {
            new(0, 98900, 0.00),
            new(98900, 613700, 0.15),
            new(613700, double.PositiveInfinity, 0.20)
        };

        // Medicare IRMAA, 2026, married filing jointly: the same per-person surcharges as single, at the joint
        // thresholds. Each spouse on Medicare pays them. Source: CMS 2026 premiums.
        public static readonly IReadOnlyList<IrmaaTier> MedicareIrmaaMarriedJoint2026 = new List<IrmaaTier>
        {
            new(218000, 81.20 + 14.50),
            new(274000, 202.90 + 37.50),
            new(342000, 324.60 + 60.40),
            new(410000, 446.30 + 83.30),
            new(750000, 487.00 + 91.00)
        };

        private static readonly FilingTables Single = new(Single2026, CapitalGainsSingle2026, MedicareIrmaaSingle2026, 1);
        private static readonly FilingTables MarriedJoint = new(MarriedJoint2026, CapitalGainsMarriedJoint2026, MedicareIrmaaMarriedJoint2026, 2);

        // The tables a household filing with `status` uses.
        public static FilingTables For(FilingStatus status) => status switch
        {
            FilingStatus.Single => Single,
            FilingStatus.MarriedJoint => MarriedJoint,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }
}
