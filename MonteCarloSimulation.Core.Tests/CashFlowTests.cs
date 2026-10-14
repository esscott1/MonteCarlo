namespace MonteCarloSimulation.Core.Tests
{
    // The Scenario runner's year table shows each year's total withdrawals (income included), its total taxes, and the
    // net between them. That net must be exactly the money the year used: its spending, any Medicare IRMAA surcharge,
    // and whatever income beyond spending was saved to Brokerage.
    public class CashFlowTests
    {
        public static TheoryData<WithdrawalStrategy, ConversionTaxFunding> Rules()
        {
            var data = new TheoryData<WithdrawalStrategy, ConversionTaxFunding>();
            foreach (var order in new[] { WithdrawalStrategy.TaxOptimized, WithdrawalStrategy.ProRata })
                foreach (var funding in Enum.GetValues<ConversionTaxFunding>())
                    data.Add(order, funding);
            return data;
        }

        [Theory]
        [MemberData(nameof(Rules))]
        public void EveryYear_WithdrawalsLessTaxes_AreTheMoneyTheYearUsed(WithdrawalStrategy order, ConversionTaxFunding funding)
        {
            // Retires at 57 (the 59.5 gate), Social Security from 67, RMDs from 75, a tax-deferred annuity paying more
            // than the early years need, and conversions to the top of 22% (big enough to push the year's gains up)
            var parameters = new SimulationParameters
            {
                Years = 30, Iterations = 1, Withdrawal = 75_000,
                Birthdate = new DateOnly(1970, 3, 1), RetirementDate = new DateOnly(2027, 1, 1),
                InitialTaxableBalance = 900_000, InitialRothBasis = 20_000, InitialRothUnrealizedGain = 10_000,
                InitialBrokerageBasis = 100_000, InitialBrokerageUnrealizedGain = 300_000,
                Mean = 0.07, StdDev = 0.12, NewMoney = 100_000, YearNewMoney = 5,
                SocialSecurityStartDate = new DateOnly(2037, 3, 1), SocialSecurityMonthlyAmount = 2_750,
                AnnualStandardDeduction = 16_000, EnableRothConversions = true, ConversionTaxFunding = funding,
                WithdrawalStrategy = order,
                Annuity = new Annuity { MonthlyPayment = 4_900, FirstPayment = new DateOnly(2027, 1, 1), Basis = 0, AnnualIncrease = 0.02 },
                ScenarioDescription = "cash flow"
            };
            var timeline = RetirementTimeline.Build(parameters);
            var strategy = WithdrawalStrategies.For(order);
            var ceiling = ConversionTargets.CeilingFor(RothConversionTarget.Bracket22);
            int years = 0;

            for (int seed = 0; seed < 20; seed++)
            {
                var run = RunSimulator.Simulate(parameters, timeline, strategy, new Random(seed), ceiling, out _);
                foreach (var y in run.Years)
                {
                    if (run.FailureYear == y.Year) continue; // a shortfall year couldn't cover its need
                    double withdrawals = y.SocialSecurityIncome + y.AnnuityIncome + y.TaxableWithdrawal + y.BrokerageWithdrawal
                        + y.RothWithdrawal + y.RothConversionTaxWithheld;
                    double taxes = y.OrdinaryTaxAmount + y.CapitalGainsTaxAmount;
                    double used = y.Withdrawal + y.IrmaaSurcharge + y.IncomeSurplusToBrokerage + y.RmdSurplusToBrokerage;
                    Assert.Equal(used, withdrawals - taxes, 4);
                    years++;
                }
            }
            Assert.True(years > 300);
        }
    }
}
