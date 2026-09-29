namespace MonteCarloSimulation.Core.Tests
{
    // Exact-value tests for the engine's internal building blocks. Unlike MonteCarloEngineTests these need no
    // randomness: each component is deterministic given its inputs.
    public class ComponentTests
    {
        private static TaxYear TaxYear(double standardDeduction, double inflationFactor = 1.0) =>
            new(standardDeduction, inflationFactor, FederalTaxBrackets.Single2026);

        private static Accounts Accounts(
            double taxable = 0, double rothBasis = 0, double rothGain = 0, double brokerageBasis = 0, double brokerageGain = 0) =>
            Core.Accounts.FromParameters(new SimulationParameters
            {
                InitialTaxableBalance = taxable,
                InitialRothBasis = rothBasis,
                InitialRothUnrealizedGain = rothGain,
                InitialBrokerageBasis = brokerageBasis,
                InitialBrokerageUnrealizedGain = brokerageGain,
                ScenarioDescription = "component test"
            });

        // --- TaxYear ---

        [Theory]
        [InlineData(0, 1.0, 0, 5_000)]
        [InlineData(16_000, 1.0, 0, 60_000)]           // crosses the deduction, 10% and 12% brackets
        [InlineData(16_000, 1.025, 25_500, 30_000)]    // stacked on top of taxable Social Security
        [InlineData(16_000, 1.3, 90_000, 400_000)]     // deep into the upper brackets
        [InlineData(0, 1.0, 0, 2_000_000)]             // into the top (unbounded) bracket
        public void GrossUpOrdinary_RoundTripsAgainstForwardTax(double deduction, double factor, double baseIncome, double desiredNet)
        {
            var taxYear = TaxYear(deduction, factor);

            double gross = taxYear.GrossUpOrdinary(desiredNet, baseIncome);

            Assert.Equal(desiredNet, gross - taxYear.IncrementalOrdinaryTax(baseIncome, gross), 6);
        }

        [Fact]
        public void BracketRoom_IncomeExactlyOnThreshold_BelongsToLowerBracket()
        {
            var room = TaxYear(16_000).BracketRoom(16_000 + 50_400);

            Assert.Equal(0.12, room.CurrentBracketRate);
            Assert.Equal(0, room.AmountUntilNextBracket!.Value, 6);
            Assert.Equal(0.22, room.NextBracketRate);
        }

        [Fact]
        public void BracketRoom_OneDollarOver_IsInNextBracket()
        {
            var room = TaxYear(16_000).BracketRoom(16_000 + 50_401);

            Assert.Equal(0.22, room.CurrentBracketRate);
            Assert.Equal(105_700 - 50_401, room.AmountUntilNextBracket!.Value, 6);
            Assert.Equal(0.24, room.NextBracketRate);
        }

        [Fact]
        public void Bracket22CeilingGross_IsDeductionPlusInflatedBracketFloor()
        {
            Assert.Equal(16_000 + 50_400 * 1.1, TaxYear(16_000, 1.1).Bracket22CeilingGross, 6);
        }

        // --- Accounts ---

        [Fact]
        public void SellBrokerage_ShrinksBasisProportionally_KeepingGainFraction()
        {
            var accounts = Accounts(brokerageBasis: 40_000, brokerageGain: 60_000);
            Assert.Equal(0.6, accounts.BrokerageGainFraction, 12);

            accounts.SellBrokerage(30_000);

            Assert.Equal(70_000, accounts.Brokerage, 6);
            Assert.Equal(28_000, accounts.BrokerageBasis, 6);
            Assert.Equal(0.6, accounts.BrokerageGainFraction, 12);
        }

        [Fact]
        public void WithdrawRoth_ShrinksBasisProportionally()
        {
            var accounts = Accounts(rothBasis: 20_000, rothGain: 80_000);

            accounts.WithdrawRoth(50_000);

            Assert.Equal(50_000, accounts.Roth, 6);
            Assert.Equal(10_000, accounts.RothBasis, 6);
        }

        [Fact]
        public void DepositAndConvert_AddToBasis()
        {
            var accounts = Accounts(taxable: 100_000, brokerageBasis: 10_000, brokerageGain: 10_000);

            accounts.DepositBrokerageCash(5_000);
            accounts.ConvertToRoth(7_000);

            Assert.Equal(25_000, accounts.Brokerage, 6);
            Assert.Equal(15_000, accounts.BrokerageBasis, 6);
            Assert.Equal(93_000, accounts.Taxable, 6);
            Assert.Equal(7_000, accounts.Roth, 6);
            Assert.Equal(7_000, accounts.RothBasis, 6);
        }

        [Fact]
        public void AgeGate_LocksTaxDeferredAndRothGains_UntilEligible()
        {
            var accounts = Accounts(taxable: 500_000, rothBasis: 20_000, rothGain: 80_000);

            Assert.Equal(0, accounts.EligibleTaxable(ageEligible: false));
            Assert.Equal(20_000, accounts.EligibleRoth(ageEligible: false));
            Assert.Equal(500_000, accounts.EligibleTaxable(ageEligible: true));
            Assert.Equal(100_000, accounts.EligibleRoth(ageEligible: true));
        }

        // --- Withdrawal strategies ---

        private static WithdrawalContext Context(
            double need, double eligibleTaxable, double brokerage, double eligibleRoth,
            double gainFraction = 0, double ssTaxable = 0, double deduction = 0) =>
            new(need, ssTaxable, TaxYear(deduction), eligibleTaxable, brokerage, gainFraction, eligibleRoth);

        [Fact]
        public void ProRata_SplitsNeedByEligibleBalance()
        {
            // A deduction larger than any draw makes the Tax Deferred share untaxed, so net == gross.
            var plan = new ProRataWithdrawalStrategy().Plan(Context(need: 60_000, eligibleTaxable: 200_000, brokerage: 100_000, eligibleRoth: 0, deduction: 1e9));

            Assert.Equal(40_000, plan.GrossTaxable, 6);
            Assert.Equal(20_000, plan.GrossBrokerage, 6);
            Assert.Equal(0, plan.Roth);
            Assert.False(plan.IsShortfall);
        }

        [Fact]
        public void ProRata_ShortfallWhenEligibleTotalBelowNeed()
        {
            var plan = new ProRataWithdrawalStrategy().Plan(Context(need: 250_000, eligibleTaxable: 0, brokerage: 100_000, eligibleRoth: 50_000));

            Assert.True(plan.IsShortfall);
            Assert.Equal(100_000, plan.NetBrokerage, 6);
            Assert.Equal(50_000, plan.Roth, 6);
        }

        [Fact]
        public void TaxOptimized_FillsTaxDeferredTo22PercentLine_BeforeBrokerage()
        {
            // No deduction: the 22% line is 50,400 gross, which nets 50,400 - (1,240 + 4,560) = 44,600.
            var plan = new TaxOptimizedWithdrawalStrategy().Plan(Context(need: 80_000, eligibleTaxable: 1_000_000, brokerage: 300_000, eligibleRoth: 100_000));

            Assert.Equal(50_400, plan.GrossTaxable, 6);
            Assert.Equal(44_600, plan.NetTaxable, 6);
            Assert.Equal(80_000 - 44_600, plan.NetBrokerage, 6);
            Assert.Equal(0, plan.Roth);
            Assert.False(plan.IsShortfall);
        }

        [Fact]
        public void TaxOptimized_MoreTaxDeferred_BeforeRoth_OnceBrokerageRunsOut()
        {
            var plan = new TaxOptimizedWithdrawalStrategy().Plan(Context(need: 80_000, eligibleTaxable: 1_000_000, brokerage: 10_000, eligibleRoth: 100_000));

            Assert.Equal(10_000, plan.GrossBrokerage, 6);
            Assert.Equal(70_000, plan.NetTaxable, 6);
            Assert.True(plan.GrossTaxable > 50_400);
            Assert.Equal(0, plan.Roth);
        }

        [Fact]
        public void TaxOptimized_RothIsLastResort()
        {
            // Before 59.5: no Tax Deferred, so Brokerage first, then Roth.
            var plan = new TaxOptimizedWithdrawalStrategy().Plan(Context(need: 30_000, eligibleTaxable: 0, brokerage: 10_000, eligibleRoth: 100_000));

            Assert.Equal(0, plan.GrossTaxable);
            Assert.Equal(10_000, plan.NetBrokerage, 6);
            Assert.Equal(20_000, plan.Roth, 6);
            Assert.False(plan.IsShortfall);
        }

        // --- Roth conversion ---

        [Fact]
        public void RothConversion_FillsTo22PercentLine_PayingTaxFromBrokerage()
        {
            var accounts = Accounts(taxable: 500_000, brokerageBasis: 100_000);

            var conversion = RothConversion.Apply(accounts, TaxYear(0), ordinaryIncomeSoFar: 0);

            Assert.Equal(50_400, conversion.Amount, 6);
            Assert.Equal(5_800, conversion.Tax, 6);
            Assert.Equal(5_800, conversion.TaxSale, 6); // all-basis Brokerage, so no LTCG on the sale
            Assert.Equal(100_000 - 5_800, accounts.Brokerage, 6);
            Assert.Equal(50_400, accounts.RothBasis, 6);
        }

        [Fact]
        public void RothConversion_ScaledDown_WhenBrokerageCannotCoverTax()
        {
            var accounts = Accounts(taxable: 500_000, brokerageBasis: 1_000);

            var conversion = RothConversion.Apply(accounts, TaxYear(0), ordinaryIncomeSoFar: 0);

            Assert.InRange(conversion.Amount, 1, 50_400 - 1);
            Assert.True(conversion.Tax <= 1_000 + 1e-9);
            Assert.True(accounts.Brokerage >= -1e-9);
            Assert.Equal(conversion.Amount, accounts.Roth, 6);
        }

        // --- Social Security ---

        [Fact]
        public void SocialSecurity_NetReducesNeed_AndExcessIsSurplus()
        {
            // 85% of 30,000 = 25,500 taxable; 9,100 above the 16,400 deduction at 10% = 910 tax; net 29,090.
            var taxYear = TaxYear(16_400, 1.025);

            var shortOfSpending = SocialSecurityYear.Compute(30_000, taxYear, spending: 50_000);
            var aboveSpending = SocialSecurityYear.Compute(30_000, taxYear, spending: 20_000);

            Assert.Equal(910, shortOfSpending.Tax, 6);
            Assert.Equal(50_000 - 29_090, shortOfSpending.Need, 6);
            Assert.Equal(0, shortOfSpending.Surplus);
            Assert.Equal(0, aboveSpending.Need);
            Assert.Equal(29_090 - 20_000, aboveSpending.Surplus, 6);
        }
    }
}
