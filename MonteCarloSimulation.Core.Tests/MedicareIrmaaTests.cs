namespace MonteCarloSimulation.Core.Tests
{
    // Medicare IRMAA: surcharges by MAGI tier (2026 single filer, inflation-scaled), a two-year lookback, only for
    // months on Medicare, none in the model's first two years, paid on top of spending.
    public class MedicareIrmaaTests
    {
        [Theory]
        [InlineData(100_000, 1.0, 0)]
        [InlineData(109_000, 1.0, 0)]                // at the threshold: not above it
        [InlineData(110_000, 1.0, 81.20 + 14.50)]
        [InlineData(150_000, 1.0, 202.90 + 37.50)]
        [InlineData(180_000, 1.0, 324.60 + 60.40)]
        [InlineData(300_000, 1.0, 446.30 + 83.30)]
        [InlineData(600_000, 1.0, 487.00 + 91.00)]
        [InlineData(115_000, 1.1, 0)]                // the threshold inflates to 119,900
        [InlineData(125_000, 1.1, (81.20 + 14.50) * 1.1)]
        public void MonthlySurcharge_FollowsThe2026Tiers_ScaledByInflation(double magi, double inflation, double expected)
        {
            Assert.Equal(expected, MedicareIrmaa.MonthlySurcharge(magi, inflation), 9);
        }

        [Theory]
        [InlineData("1961-07-15", "2026-01-01", "2027-01-01", 6)]   // 65 in July 2026: covered from July 1
        [InlineData("1961-07-15", "2027-01-01", "2028-01-01", 12)]
        [InlineData("1961-07-15", "2025-01-01", "2026-01-01", 0)]
        [InlineData("1961-07-01", "2026-03-15", "2027-01-01", 6)]   // partial year starting mid-March
        public void MedicareMonths_StartOnTheFirstOfThe65thBirthdayMonth(string birthdate, string from, string to, int expected)
        {
            Assert.Equal(expected, RetirementTimeline.MedicareMonths(DateOnly.Parse(birthdate), DateOnly.Parse(from), DateOnly.Parse(to)));
        }

        private static SimulationParameters HighIncomeRetiree(int ageAtRetirement) => new()
        {
            Years = 12,
            Iterations = 1,
            Withdrawal = 150_000,
            Birthdate = new DateOnly(2027 - ageAtRetirement, 1, 1),
            RetirementDate = new DateOnly(2027, 1, 1),
            InitialTaxableBalance = 3_000_000,
            InitialBrokerageBasis = 300_000,
            InitialBrokerageUnrealizedGain = 700_000,
            Mean = 0.05,
            StdDev = 0,
            SocialSecurityStartDate = new DateOnly(2027, 1, 1),
            SocialSecurityMonthlyAmount = 3_500,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
            ScenarioDescription = "IRMAA"
        };

        [Fact]
        public void Surcharge_UsesMagiFromTwoYearsEarlier_AndNoneInTheFirstTwoYears()
        {
            var parameters = HighIncomeRetiree(66);
            var timeline = RetirementTimeline.Build(parameters);
            var years = MonteCarloEngine.Run(parameters, new Random(1)).Result.Runs[0].Years;

            Assert.Equal(0, years[0].IrmaaSurcharge);
            Assert.Equal(0, years[1].IrmaaSurcharge);
            for (int i = 2; i < years.Count; i++)
                Assert.Equal(MedicareIrmaa.YearSurcharge(years[i - 2].Magi, timeline[i]), years[i].IrmaaSurcharge, 9);
            Assert.Contains(years, y => y.IrmaaSurcharge > 0); // this household is above the first tier
        }

        [Fact]
        public void Magi_IsOrdinaryIncomePlusEveryRealizedGain_HarvestedIncluded()
        {
            var parameters = HighIncomeRetiree(66);
            parameters.Withdrawal = 40_000; // small enough to leave 0% room, so harvesting happens
            var years = MonteCarloEngine.Run(parameters, new Random(1)).Result.Runs[0].Years;

            Assert.Contains(years, y => y.HarvestedGains > 0);
            foreach (var y in years)
            {
                double ordinary = TaxAssumptions.SsTaxableFraction * y.SocialSecurityIncome + y.TaxableWithdrawal + y.RothConversionAmount;
                Assert.Equal(ordinary + y.RealizedGains + y.HarvestedGains, y.Magi, 6);
            }
        }

        [Fact]
        public void Surcharge_IsPaidOnTopOfSpending()
        {
            // The same household with and without IRMAA-level income two years back: with a surcharge, the year's
            // withdrawals cover spending plus the surcharge, so the portfolio ends lower.
            var parameters = HighIncomeRetiree(66);
            var years = MonteCarloEngine.Run(parameters, new Random(1)).Result.Runs[0].Years;
            var charged = years.First(y => y.IrmaaSurcharge > 0);

            Assert.Equal(parameters.Withdrawal * RetirementTimeline.Build(parameters)[charged.Year].InflationFactor, charged.Withdrawal, 6);
            double cashOut = charged.TaxableWithdrawal + charged.BrokerageWithdrawal + charged.RothWithdrawal + charged.SocialSecurityIncome;
            double taxes = charged.OrdinaryTaxAmount + charged.CapitalGainsTaxAmount;
            // Everything drawn (after tax, less the conversion's own funding sale) pays for spending plus the surcharge
            Assert.True(cashOut - taxes >= charged.Withdrawal + charged.IrmaaSurcharge - 1);
        }

        [Fact]
        public void UnderSixtyFive_NeverPaysIrmaa()
        {
            var parameters = HighIncomeRetiree(50);
            var years = MonteCarloEngine.Run(parameters, new Random(1)).Result.Runs[0].Years;
            Assert.All(years.Where(y => y.AgeInYear < 64.9), y => Assert.Equal(0, y.IrmaaSurcharge));
        }
    }
}
