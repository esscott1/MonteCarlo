using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // The schedule study's Roth conversion policies: "convert to line A while a condition holds, then to line B". The
    // condition is checked every year, after that year's withdrawals, and uses only what the household knows that
    // year - its age, whether Social Security or RMDs have started, how much of its money is still Tax Deferred -
    // never what its market will do next.
    internal static class ConversionSchedules
    {
        // A conversion line: the ceiling a year fills to, or null for no conversion.
        internal sealed record Line(string Code, string Name, ConversionCeiling? Ceiling);

        // When the schedule is still on line A, given the household's parameters: a test of the year and its balances.
        internal sealed record Condition(string Code, string Name, Func<SimulationParameters, Func<RetirementYear, Accounts, bool>> For);

        public static readonly IReadOnlyList<Line> Lines =
        [
            new("0", "no conversion", null),
            new("12", "the 12% fill", RothConversion.DefaultCeiling),
            new("22", "the top of the 22% bracket", ConversionTargets.TopOf22),
            new("24", "the top of the 24% bracket", ConversionTargets.TopOf24),
        ];

        public static readonly IReadOnlyList<Condition> Conditions =
        [
            new("SS", "before Social Security starts",
                p => (year, _) => year.SocialSecurityPayments == 0 && year.Start < p.SocialSecurityStartDate),
            new("63", "before 63 (from 63, income sets Medicare surcharges two years later)", _ => (year, _) => year.AgeAtStart < 63),
            new("65", "before 65", _ => (year, _) => year.AgeAtStart < 65),
            new("70", "before 70", _ => (year, _) => year.AgeAtStart < 70),
            new("RMD", "before required minimum distributions start", _ => (year, _) => year.RmdDivisor is null),
            new("TD50", "while Tax Deferred is more than half the portfolio", _ => (_, accounts) => accounts.Taxable > 0.5 * accounts.Total),
            new("TD25", "while Tax Deferred is more than a quarter of the portfolio", _ => (_, accounts) => accounts.Taxable > 0.25 * accounts.Total),
        ];

        // Every ordered pair of different lines under every condition, coded like S24>12@SS: the top of 24% before
        // Social Security starts, then the 12% fill. Pairs that convert less early and more later are included, so the
        // "convert more early" idea is tested against its opposite.
        public static IReadOnlyList<ConversionPolicy> All { get; } =
            Conditions.SelectMany(condition => Lines.SelectMany(early => Lines
                    .Where(late => late != early)
                    .Select(late => Policy(early, late, condition))))
                .ToList();

        private static ConversionPolicy Policy(Line early, Line late, Condition condition) => new(
            $"S{early.Code}>{late.Code}@{condition.Code}",
            $"{Phrase(early)} {condition.Name}, then {Phrase(late)}",
            $"Each year {condition.Name}: {Phrase(early)}. Every other year: {Phrase(late)}.",
            Ceiling: null,
            Schedule: parameters =>
            {
                var stillEarly = condition.For(parameters);
                return (year, accounts) => stillEarly(year, accounts) ? early.Ceiling : late.Ceiling;
            });

        private static string Phrase(Line line) => line.Ceiling is null ? "no conversion" : $"convert to {line.Name}";
    }
}
