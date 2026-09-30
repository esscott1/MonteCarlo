namespace MonteCarloSimulation.Core.Tests
{
    // Exact-value tests for the engine's internal building blocks. Unlike MonteCarloEngineTests these need no
    // randomness: each component is deterministic given its inputs.
    public class ComponentTests
    {
        private static TaxYear TaxYear(double standardDeduction, double inflationFactor = 1.0) =>
            new(standardDeduction, inflationFactor, FederalTaxBrackets.Single2026, FederalTaxBrackets.CapitalGainsSingle2026);

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
        public void TaxOptimized_SmallNeed_ComesFromGainHeavyBrokerageAtZeroTax()
        {
            var plan = new TaxOptimizedWithdrawalStrategy().Plan(Context(need: 30_000, eligibleTaxable: 1_000_000, brokerage: 300_000, eligibleRoth: 100_000, gainFraction: 1));

            Assert.Equal(30_000, plan.GrossBrokerage, 6);
            Assert.Equal(0, plan.CapitalGainsTax, 9);
            Assert.Equal(0, plan.GrossTaxable);
            Assert.Equal(30_000, plan.RealizedGains, 6);
        }

        [Fact]
        public void TaxOptimized_TaxDeferredFill_StopsBeforePushingZeroRateGainsUp()
        {
            // Step 1 sells all $20k of all-gain Brokerage at 0%. Tax Deferred may then only fill to
            // 49,450 - 20,000 = 29,450 gross (nets 29,450 - 1,240 - 2,046 = 26,164) so the gains stay at 0%.
            var plan = new TaxOptimizedWithdrawalStrategy().Plan(Context(need: 45_000, eligibleTaxable: 1_000_000, brokerage: 20_000, eligibleRoth: 100_000, gainFraction: 1));

            Assert.Equal(20_000, plan.GrossBrokerage, 6);
            Assert.Equal(25_000, plan.NetTaxable, 6);
            Assert.True(plan.GrossTaxable + 20_000 <= 49_450 + 1e-6);
            Assert.Equal(0, plan.CapitalGainsTax, 9);
            Assert.Equal(0, plan.Roth);
        }

        [Fact]
        public void TaxOptimized_MoreTaxDeferred_PushesGainsUp_AndStillNetsTheNeed()
        {
            // $49,450 of gain at 0%, $10,550 more at 15%, then Tax Deferred - which stacks beneath the gains,
            // pushing more of them into 15%. The bisection accounts for that and nets exactly the need.
            var context = Context(need: 80_000, eligibleTaxable: 1_000_000, brokerage: 60_000, eligibleRoth: 100_000, gainFraction: 1);
            var plan = new TaxOptimizedWithdrawalStrategy().Plan(context);

            Assert.Equal(60_000, plan.GrossBrokerage, 6);
            Assert.Equal(80_000, plan.NetTaxable + plan.NetBrokerage, 4);
            Assert.Equal(context.TaxYear.CapitalGainsTax(plan.GrossTaxable, 60_000), plan.CapitalGainsTax, 6);
            Assert.True(plan.CapitalGainsTax > 0.15 * 10_550 + 1);
            Assert.Equal(0, plan.Roth);
            Assert.False(plan.IsShortfall);
        }

        [Fact]
        public void TaxOptimized_AllBasisBrokerage_IsDrawnFirst_ThenTaxDeferredFillsTo22PercentLine()
        {
            // All-basis Brokerage realizes no gain, so step 1 isn't capped by the 0% band. With $10k of it, Tax
            // Deferred fills the 22% line (50,400 gross nets 50,400 - 1,240 - 4,560 = 44,600), then more.
            var plan = new TaxOptimizedWithdrawalStrategy().Plan(Context(need: 50_000, eligibleTaxable: 1_000_000, brokerage: 10_000, eligibleRoth: 100_000));

            Assert.Equal(10_000, plan.GrossBrokerage, 6);
            Assert.Equal(40_000, plan.NetTaxable, 6);
            Assert.True(plan.GrossTaxable < 50_400);
        }

        [Fact]
        public void TaxOptimized_MoreTaxDeferred_BeforeRoth_OnceBrokerageRunsOut()
        {
            var plan = new TaxOptimizedWithdrawalStrategy().Plan(Context(need: 80_000, eligibleTaxable: 1_000_000, brokerage: 10_000, eligibleRoth: 100_000));

            Assert.Equal(10_000, plan.GrossBrokerage, 6);
            Assert.Equal(70_000, plan.NetTaxable, 4);
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

        // --- Capital gains (TaxYear) ---

        [Theory]
        [InlineData(0, 1.0, 0, 40_000, 0)]                  // entirely in the 0% band
        [InlineData(0, 1.0, 30_000, 30_000, 1_582.5)]       // 19,450 at 0%, 10,550 at 15%
        [InlineData(16_000, 1.0, 10_000, 60_000, 682.5)]    // 6,000 sheltered by unused deduction, 49,450 at 0%, 4,550 at 15%
        [InlineData(0, 1.0, 540_000, 10_000, 1_725)]        // 5,500 at 15%, 4,500 at 20%
        [InlineData(0, 1.0, 600_000, 10_000, 2_000)]        // all at 20%
        [InlineData(0, 1.1, 0, 60_000, 840.75)]             // 0% band inflated to 54,395
        public void CapitalGainsTax_StacksOnOrdinaryIncome(double deduction, double factor, double ordinary, double gains, double expected)
        {
            Assert.Equal(expected, TaxYear(deduction, factor).CapitalGainsTax(ordinary, gains), 6);
        }

        [Theory]
        [InlineData(0, 1.0, 0, 0, 1.0, 30_000)]
        [InlineData(0, 1.0, 0, 0, 1.0, 80_000)]             // crosses 0% -> 15%
        [InlineData(16_000, 1.025, 25_500, 10_000, 0.4, 60_000)]
        [InlineData(0, 1.0, 45_000, 0, 0.7, 700_000)]       // crosses 15% -> 20%
        [InlineData(0, 1.0, 0, 0, 0.0, 12_345)]             // all basis: gross == net
        public void GrossUpBrokerageSale_RoundTripsAgainstForwardTax(
            double deduction, double factor, double ordinary, double gainsSoFar, double gainFraction, double desiredNet)
        {
            var taxYear = TaxYear(deduction, factor);

            double sale = taxYear.GrossUpBrokerageSale(desiredNet, gainFraction, ordinary, gainsSoFar);
            double tax = taxYear.CapitalGainsTax(ordinary, gainsSoFar + sale * gainFraction) - taxYear.CapitalGainsTax(ordinary, gainsSoFar);

            Assert.Equal(desiredNet, sale - tax, 6);
        }

        [Fact]
        public void BrokerageNetCapacity_NetsTheWholeBalanceAfterStackedGainsTax()
        {
            // 100k of all-gain Brokerage, no other income: 49,450 at 0%, 50,550 at 15%.
            Assert.Equal(100_000 - 0.15 * 50_550, TaxYear(0).BrokerageNetCapacity(100_000, 1, 0, 0), 6);
        }

        [Fact]
        public void CapitalGainsBracketRoom_ReportsMarginalRateAndRoom()
        {
            var taxYear = TaxYear(0);

            Assert.Equal((0.0, (double?)0, (double?)0.15), Rounded(taxYear.CapitalGainsBracketRoom(0, 49_450)));
            Assert.Equal((0.15, (double?)(545_500 - 49_451), (double?)0.20), Rounded(taxYear.CapitalGainsBracketRoom(0, 49_451)));
            Assert.Equal((0.20, (double?)null, (double?)null), taxYear.CapitalGainsBracketRoom(600_000, 1));

            // Unused deduction is a 0% segment ahead of the 0% bracket; they read as one band.
            var withDeduction = TaxYear(16_000).CapitalGainsBracketRoom(10_000, 0);
            Assert.Equal(0, withDeduction.CurrentRate);
            Assert.Equal(6_000 + 49_450, withDeduction.AmountUntilNextBracket!.Value, 6);
            Assert.Equal(0.15, withDeduction.NextRate);
        }

        private static (double, double?, double?) Rounded((double Rate, double? Until, double? Next) room) =>
            (room.Rate, room.Until.HasValue ? Math.Round(room.Until.Value, 6) : null, room.Next);

        [Fact]
        public void OrdinaryFillCeiling_ProtectsOnlyZeroRateGains()
        {
            var taxYear = TaxYear(0);

            Assert.Equal(50_400, taxYear.OrdinaryFillCeiling(0, 0), 6);               // no gains: the 22% line
            Assert.Equal(29_450, taxYear.OrdinaryFillCeiling(0, 20_000), 6);          // keep $20k of gains at 0%
            Assert.Equal(50_400, taxYear.OrdinaryFillCeiling(49_450, 20_000), 6);     // gains already at 15%: nothing to protect
        }

        // --- Gain harvesting ---

        [Fact]
        public void GainHarvest_StepsUpBasisByLeftoverZeroRateRoom()
        {
            var accounts = Accounts(brokerageBasis: 10_000, brokerageGain: 90_000);

            double harvested = GainHarvest.Apply(accounts, TaxYear(0), ordinaryIncome: 20_000, realizedGains: 5_000);

            Assert.Equal(49_450 - 20_000 - 5_000, harvested, 6);
            Assert.Equal(10_000 + harvested, accounts.BrokerageBasis, 6);
            Assert.Equal(100_000, accounts.Brokerage, 6); // no cash moves
        }

        [Fact]
        public void GainHarvest_CappedByEmbeddedGain_AndZeroWithoutRoom()
        {
            var smallGain = Accounts(brokerageBasis: 90_000, brokerageGain: 10_000);
            Assert.Equal(10_000, GainHarvest.Apply(smallGain, TaxYear(0), 0, 0), 6);

            var noRoom = Accounts(brokerageBasis: 10_000, brokerageGain: 90_000);
            Assert.Equal(0, GainHarvest.Apply(noRoom, TaxYear(0), ordinaryIncome: 60_000, realizedGains: 0));
            Assert.Equal(10_000, noRoom.BrokerageBasis, 6);
        }

        // --- Roth conversion ---

        [Fact]
        public void RothConversion_StopsWhereItWouldPushZeroRateGainsUp()
        {
            var accounts = Accounts(taxable: 500_000, brokerageBasis: 100_000);
            var taxYear = TaxYear(0);

            var conversion = RothConversion.Apply(accounts, taxYear, ordinaryIncomeSoFar: 0, gainsSoFar: 20_000);

            Assert.Equal(29_450, conversion.Amount, 6);
            Assert.Equal(0, taxYear.CapitalGainsTax(conversion.Amount, 20_000), 9);
        }

        [Fact]
        public void RothConversion_CostIncludesPushingExistingGainsIntoHigherBracket()
        {
            // Ordinary 49,500 with $500k of gains stacked above: 4,000 of them already at 20%. Converting the
            // last 900 to the 22% line (50,400) pushes 900 more gains from 15% to 20% - a $45 bump - on top of
            // the conversion's $108 ordinary tax. All-basis Brokerage funds it, so the sale is exactly the cost.
            var accounts = Accounts(taxable: 500_000, brokerageBasis: 100_000);

            var conversion = RothConversion.Apply(accounts, TaxYear(0), ordinaryIncomeSoFar: 49_500, gainsSoFar: 500_000);

            Assert.Equal(900, conversion.Amount, 6);
            Assert.Equal(108, conversion.Tax, 6);
            Assert.Equal(108 + 45, conversion.TaxSale, 6);
            Assert.Equal(45, conversion.CapitalGainsTax, 6);
        }

        [Fact]
        public void YearTaxAttribution_SumsToTotalTax()
        {
            // The per-step attribution (strategy plan + conversion) must equal the tax on the year's final totals.
            var accounts = Accounts(taxable: 800_000, brokerageBasis: 30_000, brokerageGain: 70_000);
            var taxYear = TaxYear(16_000, 1.025);
            double ssTaxable = 20_000;
            var context = new WithdrawalContext(90_000, ssTaxable, taxYear, accounts.Taxable, accounts.Brokerage, accounts.BrokerageGainFraction, 0);

            var plan = new TaxOptimizedWithdrawalStrategy().Plan(context);
            accounts.WithdrawTaxable(plan.GrossTaxable);
            accounts.SellBrokerage(plan.GrossBrokerage);
            var conversion = RothConversion.Apply(accounts, taxYear, ssTaxable + plan.GrossTaxable, plan.RealizedGains);

            double ordinary = ssTaxable + plan.GrossTaxable + conversion.Amount;
            double gains = plan.RealizedGains + conversion.RealizedGains;
            Assert.Equal(taxYear.OrdinaryTax(ssTaxable) + plan.TaxableTax + conversion.Tax, taxYear.OrdinaryTax(ordinary), 6);
            Assert.Equal(plan.CapitalGainsTax + conversion.CapitalGainsTax, taxYear.CapitalGainsTax(ordinary, gains), 6);
        }

        [Fact]
        public void RothConversion_FillsTo22PercentLine_PayingTaxFromBrokerage()
        {
            var accounts = Accounts(taxable: 500_000, brokerageBasis: 100_000);

            var conversion = RothConversion.Apply(accounts, TaxYear(0), ordinaryIncomeSoFar: 0, gainsSoFar: 0);

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

            var conversion = RothConversion.Apply(accounts, TaxYear(0), ordinaryIncomeSoFar: 0, gainsSoFar: 0);

            Assert.InRange(conversion.Amount, 1, 50_400 - 1);
            Assert.True(conversion.Tax <= 1_000 + 1e-9);
            Assert.True(accounts.Brokerage >= -1e-9);
            Assert.Equal(conversion.Amount, accounts.Roth, 6);
        }

        // --- Conversion tax funding ---

        private static RothConversion Convert(Accounts accounts, ConversionTaxFunding rule, bool ageEligible = true, double bridgeNeed = 0,
            double ordinaryIncomeSoFar = 0, double gainsSoFar = 0, double deduction = 0) =>
            RothConversion.Apply(accounts, TaxYear(deduction), ordinaryIncomeSoFar, gainsSoFar, RothConversion.DefaultCeiling,
                new ConversionFunding(rule, ageEligible, bridgeNeed));

        [Fact]
        public void FromConversion_PaysTheTaxOutOfTheConvertedAmount()
        {
            // 50,400 converted to the 22% line costs 1,240 + 4,560 = 5,800, withheld from the conversion itself.
            var accounts = Accounts(taxable: 500_000, brokerageBasis: 100_000);

            var conversion = Convert(accounts, ConversionTaxFunding.FromConversion);

            Assert.Equal(50_400, conversion.Amount, 6);
            Assert.Equal(5_800, conversion.Tax, 6);
            Assert.Equal(5_800, conversion.Withheld, 6);
            Assert.Equal(0, conversion.TaxSale);
            Assert.Equal(5_800, conversion.OrdinaryTaxFromConversion, 6);
            Assert.Equal(0, conversion.OrdinaryTaxFromBrokerage, 6);
            Assert.Equal(449_600, accounts.Taxable, 6);
            Assert.Equal(44_600, accounts.Roth, 6);
            Assert.Equal(44_600, accounts.RothBasis, 6);
            Assert.Equal(100_000, accounts.Brokerage, 6); // untouched
        }

        [Fact]
        public void BrokerageThenConversion_UsesBrokerageFirst_AndNeverShrinksTheConversion()
        {
            // All-basis Brokerage of 1,000 pays 1,000 of the 5,800; the other 4,800 comes out of the conversion.
            var accounts = Accounts(taxable: 500_000, brokerageBasis: 1_000);

            var conversion = Convert(accounts, ConversionTaxFunding.BrokerageThenConversion);

            Assert.Equal(50_400, conversion.Amount, 6);
            Assert.Equal(1_000, conversion.TaxSale, 6);
            Assert.Equal(4_800, conversion.Withheld, 6);
            Assert.Equal(1_000, conversion.OrdinaryTaxFromBrokerage, 6);
            Assert.Equal(4_800, conversion.OrdinaryTaxFromConversion, 6);
            Assert.Equal(0, accounts.Brokerage, 6);
            Assert.Equal(45_600, accounts.Roth, 6);
        }

        [Fact]
        public void WithoutBrokerage_OnlyTheBrokerageRuleStalls()
        {
            foreach (var rule in Enum.GetValues<ConversionTaxFunding>())
            {
                var accounts = Accounts(taxable: 500_000);
                var conversion = Convert(accounts, rule, ageEligible: false, bridgeNeed: 200_000);
                Assert.Equal(rule == ConversionTaxFunding.Brokerage ? 0 : 50_400, conversion.Amount, 6);
            }
        }

        [Fact]
        public void BridgeAware_KeepsTheBridgeInBrokerage_BeforeTheGateOpens()
        {
            // Before 59.5 with 97,000 of spending still to come before the gate, only the 3,000 beyond it may pay tax.
            var before = Accounts(taxable: 500_000, brokerageBasis: 100_000);
            var early = Convert(before, ConversionTaxFunding.BridgeAware, ageEligible: false, bridgeNeed: 97_000);
            Assert.Equal(3_000, early.TaxSale, 6);
            Assert.Equal(2_800, early.Withheld, 6);
            Assert.Equal(97_000, before.Brokerage, 6);

            // Once the gate is open Brokerage pays it all, like BrokerageThenConversion.
            var after = Accounts(taxable: 500_000, brokerageBasis: 100_000);
            var late = Convert(after, ConversionTaxFunding.BridgeAware, ageEligible: true, bridgeNeed: 97_000);
            Assert.Equal(5_800, late.TaxSale, 6);
            Assert.Equal(0, late.Withheld, 6);
        }

        [Theory]
        [InlineData(ConversionTaxFunding.FromConversion)]
        [InlineData(ConversionTaxFunding.BrokerageThenConversion)]
        public void YearTaxAttribution_SumsToTotalTax_WhenTheConversionPaysItsOwnTax(ConversionTaxFunding rule)
        {
            // Small high-gain Brokerage: the conversion bumps the year's gains and its own sale realizes more.
            var accounts = Accounts(taxable: 800_000, brokerageBasis: 3_000, brokerageGain: 7_000);
            var taxYear = TaxYear(16_000, 1.025);
            double ssTaxable = 20_000;
            var context = new WithdrawalContext(12_000, ssTaxable, taxYear, accounts.Taxable, accounts.Brokerage, accounts.BrokerageGainFraction, 0);

            var plan = new TaxOptimizedWithdrawalStrategy().Plan(context);
            accounts.WithdrawTaxable(plan.GrossTaxable);
            accounts.SellBrokerage(plan.GrossBrokerage);
            var conversion = RothConversion.Apply(accounts, taxYear, ssTaxable + plan.GrossTaxable, plan.RealizedGains,
                RothConversion.DefaultCeiling, new ConversionFunding(rule, true, 0));

            double ordinary = ssTaxable + plan.GrossTaxable + conversion.Amount;
            double gains = plan.RealizedGains + conversion.RealizedGains;
            Assert.True(conversion.Withheld > 0);
            Assert.Equal(taxYear.OrdinaryTax(ssTaxable) + plan.TaxableTax + conversion.Tax, taxYear.OrdinaryTax(ordinary), 6);
            Assert.Equal(plan.CapitalGainsTax + conversion.CapitalGainsTax, taxYear.CapitalGainsTax(ordinary, gains), 6);
            Assert.Equal(conversion.Tax, conversion.OrdinaryTaxFromBrokerage + conversion.OrdinaryTaxFromConversion, 9);
        }

        [Theory]
        [InlineData(ConversionTaxFunding.Brokerage)]
        [InlineData(ConversionTaxFunding.FromConversion)]
        [InlineData(ConversionTaxFunding.BrokerageThenConversion)]
        [InlineData(ConversionTaxFunding.BridgeAware)]
        public void EveryYear_OrdinaryTaxSources_SumToOrdinaryTax(ConversionTaxFunding rule)
        {
            // What the main page's "who paid" footnote shows must add up to the ordinary tax line, every year.
            var parameters = new SimulationParameters
            {
                Years = 30, Iterations = 1, Withdrawal = 70_000,
                Birthdate = new DateOnly(1971, 5, 1), RetirementDate = new DateOnly(2027, 3, 1),
                InitialTaxableBalance = 1_400_000, InitialRothBasis = 20_000, InitialRothUnrealizedGain = 10_000,
                InitialBrokerageBasis = 60_000, InitialBrokerageUnrealizedGain = 90_000,
                Mean = 0.07, StdDev = 0.12, SocialSecurityStartDate = new DateOnly(2035, 5, 1), SocialSecurityMonthlyAmount = 2_500,
                AnnualStandardDeduction = 16_000, EnableRothConversions = true, ConversionTaxFunding = rule,
                WithdrawalStrategy = WithdrawalStrategy.TaxOptimized, ScenarioDescription = "tax sources"
            };
            var timeline = RetirementTimeline.Build(parameters);
            var strategy = WithdrawalStrategies.For(parameters.WithdrawalStrategy);

            for (int seed = 0; seed < 20; seed++)
                foreach (var y in RunSimulator.Simulate(parameters, timeline, strategy, new Random(seed)).Years)
                    Assert.Equal(y.OrdinaryTaxAmount,
                        y.SocialSecurityTax + y.TaxDeferredWithdrawalTax + y.RothConversionOrdinaryTaxFromBrokerage + y.RothConversionOrdinaryTaxFromConversion, 6);
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
