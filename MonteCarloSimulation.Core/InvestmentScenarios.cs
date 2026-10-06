namespace MonteCarloSimulation.Core
{
    public record InvestmentScenario(int Id, string Description, double Mean, double StdDev);

    public static class InvestmentScenarios
    {
        public static readonly IReadOnlyList<InvestmentScenario> All = new List<InvestmentScenario>
        {
            new(1, "Use the last 54 years of 10 year govt bonds", 0.0583, 0.0295),
            new(2, "Use the last 95 years of S&P returns", 0.0807, 0.1915),
            new(3, "Use the last 30 years of S&P returns", 0.1007, 0.1688),
            new(4, "Use the current 10 year bond yield", 0.0443, 0.001),
        };

        public static InvestmentScenario? ById(int id) => All.FirstOrDefault(s => s.Id == id);
    }
}
