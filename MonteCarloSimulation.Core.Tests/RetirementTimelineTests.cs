namespace MonteCarloSimulation.Core.Tests
{
    public class RetirementTimelineTests
    {
        private static SimulationParameters Parameters(
            DateOnly retire, int years, DateOnly birthdate, DateOnly? socialSecurityStart = null) => new()
            {
                Years = years,
                Iterations = 1,
                Birthdate = birthdate,
                RetirementDate = retire,
                SocialSecurityStartDate = socialSecurityStart ?? retire.AddYears(100),
                WithdrawalStrategy = WithdrawalStrategy.ProRata, // written under the old Pro-rata default
                ScenarioDescription = "timeline test"
            };

        [Fact]
        public void JanuaryFirstRetirement_GivesWholeCalendarYears_StartingAtInflationFactorOne()
        {
            var timeline = RetirementTimeline.Build(Parameters(new DateOnly(2026, 1, 1), 5, new DateOnly(1960, 1, 1)));

            Assert.Equal(new[] { 2026, 2027, 2028, 2029, 2030 }, timeline.Select(y => y.CalendarYear));
            Assert.All(timeline, y => Assert.Equal(1.0, y.Fraction));
            Assert.Equal(1.0, timeline[0].InflationFactor);
            Assert.Equal(Math.Pow(1.025, 4), timeline[4].InflationFactor, 12);
        }

        [Fact]
        public void MidYearRetirement_GivesPartialFirstAndLastYears_SummingToTheHorizon()
        {
            var timeline = RetirementTimeline.Build(Parameters(new DateOnly(2030, 7, 1), 5, new DateOnly(1969, 7, 7)));

            Assert.Equal(6, timeline.Count); // 2030 (Jul-Dec) through 2035 (Jan-Jun)
            Assert.Equal(184.0 / 365, timeline[0].Fraction, 12);
            Assert.Equal(181.0 / 365, timeline[^1].Fraction, 12);
            Assert.Equal(new DateOnly(2035, 7, 1), timeline[^1].End);
            Assert.Equal(5, timeline.Sum(y => y.Fraction), 9);
            Assert.Equal(Math.Pow(1.025, 4), timeline[0].InflationFactor, 12); // 2026 -> 2030
        }

        [Fact]
        public void Inflation_Is25PercentForTwentyRetirementYears_Then1Percent()
        {
            Assert.Equal(Math.Pow(1.025, 20), RetirementTimeline.InflationFactor(2046, 2026), 12);
            Assert.Equal(Math.Pow(1.025, 20) * 1.01, RetirementTimeline.InflationFactor(2047, 2026), 12);
            // Years before a later retirement also inflate at 2.5%
            Assert.Equal(Math.Pow(1.025, 24), RetirementTimeline.InflationFactor(2050, 2031), 12);
        }

        [Fact]
        public void Age_ComesFromBirthdateAndEachYearsStart_WithTheGateJudgedAtYearStart()
        {
            // Born Jan 1 1971, retiring Jan 1 2030: 59.0 in 2030 (locked), 60.0 in 2031 (unlocked).
            var timeline = RetirementTimeline.Build(Parameters(new DateOnly(2030, 1, 1), 3, new DateOnly(1971, 1, 1)));

            Assert.Equal(59.0, timeline[0].AgeAtStart, 2);
            Assert.False(timeline[0].AgeEligible);
            Assert.True(timeline[1].AgeEligible);

            // Mid-year retirement: age at the retirement date itself.
            var midYear = RetirementTimeline.Build(Parameters(new DateOnly(2030, 7, 1), 1, new DateOnly(1969, 7, 7)));
            Assert.Equal(60.98, midYear[0].AgeAtStart, 2);
        }

        [Fact]
        public void SocialSecurity_PartialFirstYear_CountsTheMonthlyPaymentsReceived()
        {
            // Born 7/7/1969, first payment 8/7/2031: Aug-Dec 2031 = 5 payments, then 12 a year; the final partial
            // year (Jan 1 - Jul 1 2035) holds Jan-Jun = 6.
            var timeline = RetirementTimeline.Build(Parameters(
                new DateOnly(2030, 7, 1), 5, new DateOnly(1969, 7, 7), socialSecurityStart: new DateOnly(2031, 8, 7)));

            Assert.Equal(new[] { 0, 5, 12, 12, 12, 6 }, timeline.Select(y => y.SocialSecurityPayments));
        }

        [Theory]
        [InlineData("2031-01-31", "2031-01-01", "2032-01-01", 12)] // month-end start clamps to Feb 28 etc.
        [InlineData("2025-06-15", "2030-07-01", "2031-01-01", 6)]  // started before this window: Jul-Dec
        [InlineData("2031-08-07", "2030-07-01", "2031-01-01", 0)]  // starts after the window
        [InlineData("2031-08-07", "2031-08-07", "2031-08-08", 1)]  // the first payment date itself counts
        public void SocialSecurityPayments_CountsPaymentDatesInWindow(string first, string from, string to, int expected)
        {
            Assert.Equal(expected, RetirementTimeline.SocialSecurityPayments(DateOnly.Parse(first), DateOnly.Parse(from), DateOnly.Parse(to)));
        }

        [Fact]
        public void Engine_MidYearRetirement_ProratesSpendingAndReturns_AndPaysPartialSocialSecurity()
        {
            var parameters = new SimulationParameters
            {
                Years = 5,
                Iterations = 1,
                Withdrawal = 40_000,
                Birthdate = new DateOnly(1969, 7, 7),
                RetirementDate = new DateOnly(2030, 7, 1),
                InitialBrokerageBasis = 2_000_000, // all basis: Brokerage covers spending tax-free
                Mean = 0.06,
                StdDev = 0,
                SocialSecurityStartDate = new DateOnly(2031, 8, 7),
                SocialSecurityMonthlyAmount = 2_500,
                WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
                ScenarioDescription = "Mid-year retirement"
            };

            var years = MonteCarloEngine.Run(parameters).Result.Runs[0].Years;
            double factor2030 = Math.Pow(1.025, 4);
            double firstYearFraction = 184.0 / 365;

            Assert.Equal(6, years.Count);
            Assert.Equal(2030, years[0].CalendarYear);
            Assert.Equal(firstYearFraction, years[0].YearFraction, 12);
            Assert.Equal(40_000 * factor2030 * firstYearFraction, years[0].Withdrawal, 6);
            Assert.Equal(40_000 * factor2030 * firstYearFraction, years[0].BrokerageWithdrawal, 6);
            // Half a year of a 6% return on the starting balance, less the half year of spending
            Assert.Equal(2_000_000 * (1 + 0.06 * firstYearFraction) - years[0].BrokerageWithdrawal, years[0].BrokerageBalance, 4);
            Assert.Equal(0, years[0].SocialSecurityMonths);

            Assert.Equal(5, years[1].SocialSecurityMonths);
            Assert.Equal(2_500 * factor2030 * 1.025 * 5, years[1].SocialSecurityIncome, 6);
            Assert.Equal(12, years[2].SocialSecurityMonths);
        }
    }
}
