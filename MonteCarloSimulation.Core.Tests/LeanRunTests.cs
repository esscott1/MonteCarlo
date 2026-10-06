namespace MonteCarloSimulation.Core.Tests
{
    // Searches (AutomaticStrategy, the Optimizer) run RunSimulator.Survives: each path's returns drawn up front and no
    // year-by-year report. It must match the full Simulate exactly - same outcome, same final balances - for every
    // order, conversion target and conversion-tax funding rule, with and without the 59.5 gate in play.
    public class LeanRunTests
    {
        private static SimulationParameters Parameters(DateOnly birthdate, DateOnly retirementDate, double withdrawal) => new()
        {
            Years = 35,
            Iterations = 1,
            Withdrawal = withdrawal,
            Birthdate = birthdate,
            RetirementDate = retirementDate,
            InitialTaxableBalance = 1_200_000,
            InitialRothBasis = 100_000,
            InitialRothUnrealizedGain = 60_000,
            InitialBrokerageBasis = 250_000,
            InitialBrokerageUnrealizedGain = 350_000,
            Mean = 0.0807,
            StdDev = 0.1915,
            NewMoney = 200_000,
            YearNewMoney = 6,
            SocialSecurityStartDate = birthdate.AddYears(67),
            SocialSecurityMonthlyAmount = 3_100,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            ScenarioDescription = "Lean run equivalence"
        };

        internal static SimulationParameters WithAssetMix(SimulationParameters parameters)
        {
            parameters.AssetMix = new AssetMix
            {
                StockWeight = 0.6, BondWeight = 0.3, CashWeight = 0.1,
                StockMean = 0.08, StockStdDev = 0.19,
                BondMean = 0.045, BondStdDev = 0.01,
                CashMean = 0.035, CashStdDev = 0.01,
                StockBondCorrelation = 0.1
            };
            return parameters;
        }

        public static TheoryData<WithdrawalStrategy, RothConversionTarget, ConversionTaxFunding> Combinations()
        {
            var data = new TheoryData<WithdrawalStrategy, RothConversionTarget, ConversionTaxFunding>();
            foreach (var order in new[] { WithdrawalStrategy.TaxOptimized, WithdrawalStrategy.ProRata })
                foreach (var target in new[] { RothConversionTarget.None, RothConversionTarget.Bracket12, RothConversionTarget.Bracket22, RothConversionTarget.Bracket24 })
                    foreach (var funding in Enum.GetValues<ConversionTaxFunding>())
                        data.Add(order, target, funding);
            return data;
        }

        [Theory]
        [MemberData(nameof(Combinations))]
        public void Survives_MatchesSimulate_OutcomeAndFinalBalances(
            WithdrawalStrategy order, RothConversionTarget target, ConversionTaxFunding funding)
        {
            // An early retiree (years behind the 59.5 gate, Medicare and IRMAA later) and one retiring at 66, at
            // spending levels where a good share of paths fail
            var households = new[]
            {
                Parameters(new DateOnly(1975, 9, 30), new DateOnly(2027, 1, 1), 95_000),
                Parameters(new DateOnly(1961, 5, 2), new DateOnly(2027, 7, 1), 130_000),
                // The Scenario runner's stocks/bonds/cash market
                WithAssetMix(Parameters(new DateOnly(1975, 9, 30), new DateOnly(2027, 1, 1), 90_000))
            };
            var strategy = WithdrawalStrategies.For(order);
            var ceiling = ConversionTargets.CeilingFor(target);
            const int paths = 60;
            int survived = 0, failed = 0;

            foreach (var parameters in households)
            {
                parameters.ConversionTaxFunding = funding;
                var timeline = RetirementTimeline.Build(parameters);
                var allReturns = RunSimulator.SeededReturns(parameters, timeline.Count, paths);

                for (int path = 0; path < paths; path++)
                {
                    var full = RunSimulator.Simulate(parameters, timeline, strategy, new Random(path), ceiling, out var fullAccounts);
                    bool survives = RunSimulator.Survives(parameters, timeline, strategy, allReturns[path], ceiling, out var leanAccounts);

                    Assert.Equal(!full.Failed, survives);
                    if (survives) survived++; else failed++;
                    Assert.Equal(fullAccounts.Taxable, leanAccounts.Taxable);
                    Assert.Equal(fullAccounts.Brokerage, leanAccounts.Brokerage);
                    Assert.Equal(fullAccounts.BrokerageBasis, leanAccounts.BrokerageBasis);
                    Assert.Equal(fullAccounts.Roth, leanAccounts.Roth);
                    Assert.Equal(fullAccounts.RothBasis, leanAccounts.RothBasis);
                    // The precomputed returns are the ones the seeded run drew, year by year
                    for (int year = 0; year < full.Years.Count; year++)
                        Assert.Equal(full.Years[year].RateOfReturn, allReturns[path][year]);
                }
            }

            // Both outcomes are exercised, including runs that stop early
            Assert.True(survived > 0 && failed > 0, $"{survived} survived, {failed} failed");
        }

        [Fact]
        public void SeededReturns_ForOnePath_MatchTheSamePathInTheFullSet()
        {
            var all = RunSimulator.SeededReturns(0.07, 0.15, 30, 25);
            Assert.Equal(all[17], RunSimulator.SeededReturns(0.07, 0.15, 30, 17, 1)[0]);
        }
    }
}
