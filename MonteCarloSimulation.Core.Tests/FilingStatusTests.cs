namespace MonteCarloSimulation.Core.Tests
{
    // Married filing jointly: the joint 2026 tables, the tax year built from them, and a run that uses them. Single
    // stays the default, so every other test runs as a single filer.
    public class FilingStatusTests
    {
        [Theory]
        [InlineData(FilingStatus.Single, 1)]
        [InlineData(FilingStatus.MarriedJoint, 2)]
        public void EveryTable_StartsAtZero_IsContiguous_AndEndsOpen(FilingStatus status, int medicarePeople)
        {
            var tables = FederalTaxBrackets.For(status);

            foreach (var brackets in new[] { tables.Ordinary, tables.CapitalGains })
            {
                Assert.Equal(0, brackets[0].LowerBound);
                for (int i = 1; i < brackets.Count; i++)
                    Assert.Equal(brackets[i - 1].UpperBound, brackets[i].LowerBound);
                Assert.Equal(double.PositiveInfinity, brackets[^1].UpperBound);
            }
            for (int i = 1; i < tables.MedicareIrmaa.Count; i++)
                Assert.True(tables.MedicareIrmaa[i].MagiAbove > tables.MedicareIrmaa[i - 1].MagiAbove);
            Assert.Equal(medicarePeople, tables.MedicarePeople);
        }

        [Fact]
        public void MarriedTables_HaveTheSingleTablesRatesAndSurcharges()
        {
            var single = FederalTaxBrackets.For(FilingStatus.Single);
            var married = FederalTaxBrackets.For(FilingStatus.MarriedJoint);

            Assert.Equal(single.Ordinary.Select(b => b.Rate), married.Ordinary.Select(b => b.Rate));
            Assert.Equal(single.CapitalGains.Select(b => b.Rate), married.CapitalGains.Select(b => b.Rate));
            Assert.Equal(single.MedicareIrmaa.Select(t => t.MonthlySurcharge), married.MedicareIrmaa.Select(t => t.MonthlySurcharge));
        }

        private static TaxYear Married(double standardDeduction, double inflationFactor = 1.0) =>
            TaxYear.For(FilingStatus.MarriedJoint, standardDeduction, inflationFactor);

        [Fact]
        public void MarriedOrdinaryTax_FollowsTheJointBrackets()
        {
            var taxYear = Married(32_000);

            Assert.Equal(0, taxYear.OrdinaryTax(32_000), 6);
            Assert.Equal(2_480, taxYear.OrdinaryTax(32_000 + 24_800), 6);                        // 10% to 24,800
            Assert.Equal(2_480 + 9_120, taxYear.OrdinaryTax(32_000 + 100_800), 6);              // 12% to 100,800
            Assert.Equal(2_480 + 9_120 + 0.22 * 10_000, taxYear.OrdinaryTax(32_000 + 110_800), 6);
            Assert.True(taxYear.OrdinaryTax(132_800) < TaxYear.For(FilingStatus.Single, 16_000, 1.0).OrdinaryTax(132_800));
        }

        [Fact]
        public void MarriedBracketRoom_TheJoint22PercentLine()
        {
            var room = Married(32_000).BracketRoom(32_000 + 100_800);

            Assert.Equal(0.12, room.CurrentBracketRate);
            Assert.Equal(0, room.AmountUntilNextBracket!.Value, 6);
            Assert.Equal(0.22, room.NextBracketRate);
        }

        [Fact]
        public void MarriedCeilings_ComeFromTheJointTables()
        {
            var taxYear = Married(0);

            Assert.Equal(100_800, taxYear.Bracket22CeilingGross, 6);
            Assert.Equal(98_900, taxYear.ZeroRateCeilingGross, 6);
            Assert.Equal(100_800, taxYear.OrdinaryFillCeiling(0, 0), 6);              // no gains: the 22% line
            Assert.Equal(68_900, taxYear.OrdinaryFillCeiling(0, 30_000), 6);          // keep $30k of gains at 0%
            Assert.Equal(98_900, taxYear.ZeroRateGains(0, 120_000), 6);
            Assert.Equal(100_800 * 1.1 + 32_000, Married(32_000, 1.1).Bracket22CeilingGross, 6);
        }

        // A deterministic household drawing only from Tax Deferred: married pays the joint brackets on the same draw.
        private static SimulationParameters TaxDeferredOnly(FilingStatus status, double deduction) => new()
        {
            Years = 3,
            Iterations = 1,
            Withdrawal = 90_000,
            Birthdate = new DateOnly(1956, 1, 1),
            RetirementDate = new DateOnly(2026, 1, 1),
            InitialTaxableBalance = 2_000_000,
            Mean = 0.05,
            StdDev = 0,
            AnnualStandardDeduction = deduction,
            FilingStatus = status,
            WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
            RothConversionTarget = RothConversionTarget.None,
            ScenarioDescription = "Tax Deferred only"
        };

        [Fact]
        public void MarriedRun_IsTaxedOnTheJointBrackets()
        {
            var single = MonteCarloEngine.Run(TaxDeferredOnly(FilingStatus.Single, 16_000), new Random(1)).Result.Runs[0].Years[0];
            var married = MonteCarloEngine.Run(TaxDeferredOnly(FilingStatus.MarriedJoint, 32_000), new Random(1)).Result.Runs[0].Years[0];

            Assert.Equal(0.22, single.OrdinaryBracketRate);
            Assert.Equal(0.12, married.OrdinaryBracketRate);
            Assert.Equal(TaxYear.For(FilingStatus.MarriedJoint, 32_000, 1.0).OrdinaryTax(married.TaxableWithdrawal), married.OrdinaryTaxAmount, 6);
            Assert.True(married.OrdinaryTaxAmount < single.OrdinaryTaxAmount);
            Assert.True(married.TaxableWithdrawal < single.TaxableWithdrawal); // less gross-up for the same spending
        }

        [Fact]
        public void SingleStaysTheDefault()
        {
            var parameters = TaxDeferredOnly(FilingStatus.Single, 16_000);
            var unset = TaxDeferredOnly(FilingStatus.Single, 16_000);
            unset.FilingStatus = default;

            Assert.Equal(FilingStatus.Single, new SimulationParameters { ScenarioDescription = "" }.FilingStatus);
            Assert.Equal(
                MonteCarloEngine.Run(parameters, new Random(1)).Result.Runs[0].Years.Select(y => y.ToString()),
                MonteCarloEngine.Run(unset, new Random(1)).Result.Runs[0].Years.Select(y => y.ToString()));
        }
    }
}
