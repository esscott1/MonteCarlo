namespace MonteCarloSimulation.Core
{
    public record InvestmentScenario(int Id, string Description, double Mean, double StdDev, string MenuLabel);

    public static class InvestmentScenarios
    {
        public static readonly IReadOnlyList<InvestmentScenario> All = new List<InvestmentScenario>
        {
            new(1, "Use the last 54 years of 10 year govt bonds", 0.0583, 0.0295, "10 year gov bonds - 5.8% return w/std. 2.95%"),
            new(2, "Use the last 95 years of S&P returns", 0.0807, 0.1915, "Last 95 years of S&P - 8.07% return w/ std. 19.15%"),
            new(3, "Use the last 30 years of S&P returns", 0.1007, 0.1688, "Last 30 years of S&P - 10.07% return w/ std 16.8%"),
            new(4, "Use the current 10 year bond yield", 0.0443, 0.001, "Current 10 year bond yield - 4.43% return w/ std 0.1%"),
        };

        public static InvestmentScenario? ById(int id) => All.FirstOrDefault(s => s.Id == id);
    }
}
