namespace MonteCarloSimulation.Core.Tests
{
    // Required minimum distributions: the SECURE 2.0 start ages, the Uniform Lifetime Table, the year's RMD stacked on
    // Social Security, and in a run - drawn first from Tax Deferred, prorated for a partial year, with any after-tax
    // amount beyond the year's need saved to Brokerage.
    public class RequiredMinimumDistributionTests
    {
        [Theory]
        [InlineData(1950, 72)]
        [InlineData(1951, 73)]
        [InlineData(1959, 73)]
        [InlineData(1960, 75)]
        [InlineData(1975, 75)]
        public void StartAge_FollowsSecure2(int birthYear, int expected)
        {
            Assert.Equal(expected, RequiredMinimumDistributions.StartAge(new DateOnly(birthYear, 6, 15)));
        }

        [Theory]
        [InlineData(72, 27.4)]
        [InlineData(73, 26.5)]
        [InlineData(75, 24.6)]
        [InlineData(80, 20.2)]
        [InlineData(90, 12.2)]
        [InlineData(100, 6.4)]
        [InlineData(120, 2.0)]
        [InlineData(125, 2.0)]   // the last entry covers 120 and older
        public void Divisor_IsTheUniformLifetimeTable(int age, double expected)
        {
            Assert.Equal(expected, RequiredMinimumDistributions.Divisor(age));
        }

        [Fact]
        public void Divisor_BelowTheTable_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RequiredMinimumDistributions.Divisor(71));
        }

        [Fact]
        public void DivisorFor_StartsInTheYearTheStartAgeIsReached()
        {
            var born1960 = new DateOnly(1960, 11, 30);
            Assert.Null(RequiredMinimumDistributions.DivisorFor(born1960, 2034));      // 74 this year
            Assert.Equal(24.6, RequiredMinimumDistributions.DivisorFor(born1960, 2035)); // 75 this year, though not until November
        }

        [Fact]
        public void Compute_TaxesTheRmdOnTopOfTheBase_AndCoversTheNeedFirst()
        {
            // 16,000 deduction, nothing else counted: 40,000 leaves 24,000 taxable = 1,240 at 10% + 11,600 at 12% = 2,632
            var taxYear = TaxYear.For(FilingStatus.Single, 16_000, 1.0);

            var belowNeed = RequiredDistributionYear.Compute(40_000, taxYear, baseIncome: 0, need: 50_000);
            var aboveNeed = RequiredDistributionYear.Compute(40_000, taxYear, baseIncome: 0, need: 30_000);

            Assert.Equal(2_632, belowNeed.Tax, 6);
            Assert.Equal(50_000 - 37_368, belowNeed.Need, 6);
            Assert.Equal(0, belowNeed.Surplus);
            Assert.Equal(0, aboveNeed.Need);
            Assert.Equal(37_368 - 30_000, aboveNeed.Surplus, 6);

            // Stacked on 25,500 of taxable Social Security (9,500 taxable, 950 tax): 65,500 gross is 49,500 taxable,
            // 1,240 + 37,100 at 12% = 5,692 in all, so the RMD adds 4,742
            var onSocialSecurity = RequiredDistributionYear.Compute(40_000, taxYear, baseIncome: 25_500, need: 50_000);
            Assert.Equal(4_742, onSocialSecurity.Tax, 6);
        }

        [Fact]
        public void None_LeavesTheNeedAsItIs()
        {
            Assert.Equal(new RequiredDistributionYear(0, 0, 12_345, 0), RequiredDistributionYear.None(12_345));
            Assert.Equal(RequiredDistributionYear.None(12_345),
                RequiredDistributionYear.Compute(0, TaxYear.For(FilingStatus.Single, 16_000, 1.0), 0, 12_345));
        }

        // Tax Deferred only, no returns and no Social Security, spending well below the RMD
        private static SimulationParameters Retiree(DateOnly birthdate, DateOnly retirementDate, int years = 3) => new()
        {
            Years = years,
            Iterations = 1,
            Withdrawal = 20_000,
            Birthdate = birthdate,
            RetirementDate = retirementDate,
            InitialTaxableBalance = 1_000_000,
            Mean = 0,
            StdDev = 0,
            SocialSecurityStartDate = birthdate.AddYears(70),
            SocialSecurityMonthlyAmount = 0,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = false,
            WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
            ScenarioDescription = "RMD"
        };

        private static IReadOnlyList<RunYearDetail> Years(SimulationParameters parameters) =>
            MonteCarloEngine.Run(parameters, new Random(1)).Result.Runs[0].Years;

        [Fact]
        public void Run_DrawsTheRmdFirst_AndSavesWhatSpendingDoesNotNeed()
        {
            // Born 1950: 77 in 2027, divisor 22.9. The whole year's spending comes out of the RMD.
            var parameters = Retiree(new DateOnly(1950, 6, 15), new DateOnly(2027, 1, 1));
            var timeline = RetirementTimeline.Build(parameters);
            var years = Years(parameters);
            var first = years[0];

            double rmd = 1_000_000 / 22.9;
            double tax = timeline[0].TaxYear.IncrementalOrdinaryTax(0, rmd);
            double surplus = rmd - tax - first.Withdrawal;

            Assert.Equal(22.9, first.RmdDivisor);
            Assert.Equal(rmd, first.RequiredMinimumDistribution, 6);
            Assert.Equal(rmd, first.TaxableWithdrawal, 6);         // nothing beyond the RMD was needed
            Assert.Equal(tax, first.TaxDeferredWithdrawalTax, 6);
            Assert.Equal(tax, first.OrdinaryTaxAmount, 6);
            Assert.Equal(surplus, first.RmdSurplusToBrokerage, 6);
            Assert.Equal(surplus, first.BrokerageBalance, 6);      // saved to Brokerage
            Assert.Equal(1_000_000 - rmd, first.TaxableBalance, 6);

            // The next year's RMD is on the year-end balance, at 78's divisor
            Assert.Equal(22.0, years[1].RmdDivisor);
            Assert.Equal((1_000_000 - rmd) / 22.0, years[1].RequiredMinimumDistribution, 6);
        }

        [Fact]
        public void Run_ProratesThePartialFirstYear()
        {
            var parameters = Retiree(new DateOnly(1950, 6, 15), new DateOnly(2027, 7, 1));
            var first = Years(parameters)[0];

            Assert.Equal(1_000_000 / 22.9 * (184.0 / 365), first.RequiredMinimumDistribution, 6);
        }

        [Fact]
        public void Run_HasNoRmdBeforeTheStartYear()
        {
            // Born 1960: RMDs start in 2035, at 75
            var parameters = Retiree(new DateOnly(1960, 3, 1), new DateOnly(2027, 1, 1), years: 10);
            parameters.Withdrawal = 60_000;
            var years = Years(parameters);

            foreach (var year in years.Where(y => y.CalendarYear < 2035))
            {
                Assert.Null(year.RmdDivisor);
                Assert.Equal(0, year.RequiredMinimumDistribution);
                Assert.Equal(0, year.RmdSurplusToBrokerage);
            }
            var start = years.Single(y => y.CalendarYear == 2035);
            Assert.Equal(24.6, start.RmdDivisor);
            Assert.True(start.RequiredMinimumDistribution > 0);
        }
    }
}
