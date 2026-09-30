namespace MonteCarloSimulation.Core.Tests
{
    public class MonteCarloEngineTests
    {
        // Retiring on Jan 1 of the bracket-table year makes year 0 a full year with inflation factor 1.0.
        private static readonly DateOnly Retire = new(2026, 1, 1);

        [Fact]
        public void Run_AllIterationsSucceed_WhenReturnsExceedWithdrawals()
        {
            var parameters = new SimulationParameters
            {
                Years = 20,
                Iterations = 50,
                Withdrawal = 20_000,
                Birthdate = Retire.AddYears(-70),
                RetirementDate = Retire,
                InitialTaxableBalance = 500_000,
                InitialRothBasis = 250_000,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 150_000,
                InitialBrokerageUnrealizedGain = 100_000,
                Mean = 0.07,
                StdDev = 0, // every run gets exactly Mean as its return every year - fully deterministic
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Deterministic 7% return, small withdrawal"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.Equal(parameters.Iterations, output.Result.Runs.Count);
        }

        [Fact]
        public void Run_AllIterationsFail_WhenWithdrawalExceedsBalance()
        {
            var parameters = new SimulationParameters
            {
                Years = 20,
                Iterations = 50,
                Withdrawal = 500_000,
                Birthdate = Retire.AddYears(-70),
                RetirementDate = Retire,
                InitialTaxableBalance = 300_000,
                InitialRothBasis = 150_000,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 90_000,
                InitialBrokerageUnrealizedGain = 60_000,
                Mean = 0,
                StdDev = 0, // every run gets exactly 0% return every year - fully deterministic
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Deterministic 0% return, withdrawal far exceeds balance"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.Equal(parameters.Iterations, output.Result.OutOfMoneyCount);
        }

        [Fact]
        public void Run_ApproximatelyEightyPercentSucceed_WithVolatileScenario()
        {
            var parameters = new SimulationParameters
            {
                Years = 30,
                Iterations = 150,
                Withdrawal = 45_000,
                Birthdate = Retire.AddYears(-70),
                RetirementDate = Retire,
                InitialTaxableBalance = 700_000,
                InitialRothBasis = 350_000,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 210_000,
                InitialBrokerageUnrealizedGain = 140_000,
                Mean = 0.0807,
                StdDev = 0.1915, // "Last 95 years of S&P" preset volatility
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Volatile scenario calibrated to land near 80% survival"
            };

            var output = MonteCarloEngine.Run(parameters);
            double passRate = 1.0 - (output.Result.OutOfMoneyCount / (double)parameters.Iterations);

            Assert.InRange(passRate, 0.65, 0.95);
        }

        [Fact]
        public void Run_ApproximatelyTwentyPercentSucceed_WithVolatileScenario()
        {
            var parameters = new SimulationParameters
            {
                Years = 30,
                Iterations = 300,
                Withdrawal = 105_000,
                Birthdate = Retire.AddYears(-70),
                RetirementDate = Retire,
                InitialTaxableBalance = 700_000,
                InitialRothBasis = 350_000,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 210_000,
                InitialBrokerageUnrealizedGain = 140_000,
                Mean = 0.0807,
                StdDev = 0.1915, // "Last 95 years of S&P" preset volatility
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Volatile scenario calibrated to land near 20% survival"
            };

            var output = MonteCarloEngine.Run(parameters);
            double passRate = 1.0 - (output.Result.OutOfMoneyCount / (double)parameters.Iterations);

            Assert.InRange(passRate, 0.10, 0.35);
        }

        [Fact]
        public void Run_BrokerageWithdrawal_TaxedNearZero_WhenMostlyBasis()
        {
            var parameters = new SimulationParameters
            {
                Years = 10,
                Iterations = 20,
                Withdrawal = 20_000,
                Birthdate = Retire.AddYears(-70),
                RetirementDate = Retire,
                InitialTaxableBalance = 0,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 500_000,
                InitialBrokerageUnrealizedGain = 0,
                Mean = 0,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "All-basis brokerage withdrawal"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.Runs.Select(r => r.AverageTaxRate), rate => Assert.InRange(rate, 0.0, 0.01));
        }

        [Theory]
        [InlineData(20_000, 0.0, 1e-9)]    // all of the gain fits in the 0% band
        [InlineData(200_000, 0.05, 0.15)]  // the first ~$50k of gain at 0%, the rest at 15%
        public void Run_AllGainBrokerageWithdrawal_TaxedAtStackedLtcgRates(double withdrawal, double minRate, double maxRate)
        {
            var parameters = new SimulationParameters
            {
                Years = 10,
                Iterations = 20,
                Withdrawal = withdrawal,
                Birthdate = Retire.AddYears(-70),
                RetirementDate = Retire,
                InitialTaxableBalance = 0,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 0,
                InitialBrokerageUnrealizedGain = 5_000_000,
                Mean = 0,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "All-gain brokerage withdrawal"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.Runs.Select(r => r.AverageTaxRate), rate => Assert.InRange(rate, minRate, maxRate));
        }

        [Fact]
        public void Run_BrokerageTaxRate_IsHigher_WithMoreEmbeddedGain()
        {
            SimulationParameters BuildParameters(double basis, double gain) => new()
            {
                Years = 10,
                Iterations = 20,
                Withdrawal = 150_000, // large enough that high-gain sales run past the 0% band
                Birthdate = Retire.AddYears(-70),
                RetirementDate = Retire,
                InitialTaxableBalance = 0,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = basis,
                InitialBrokerageUnrealizedGain = gain,
                Mean = 0,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Basis-vs-gain contrast"
            };

            var highBasisOutput = MonteCarloEngine.Run(BuildParameters(basis: 4_500_000, gain: 500_000));
            var highGainOutput = MonteCarloEngine.Run(BuildParameters(basis: 500_000, gain: 4_500_000));

            double highBasisAvgTaxRate = highBasisOutput.Result.Runs.Average(r => r.AverageTaxRate);
            double highGainAvgTaxRate = highGainOutput.Result.Runs.Average(r => r.AverageTaxRate);

            Assert.Equal(0, highBasisAvgTaxRate, 9); // ~$15k of gain a year: all in the 0% band
            Assert.True(highGainAvgTaxRate > 0.05);
        }

        [Fact]
        public void Run_NewMoney_LandsInBrokerageAsBasis_NotGain()
        {
            var parameters = new SimulationParameters
            {
                Years = 5,
                Iterations = 10,
                Withdrawal = 20_000,
                Birthdate = Retire.AddYears(-70),
                RetirementDate = Retire,
                InitialTaxableBalance = 0,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 30_000,
                InitialBrokerageUnrealizedGain = 0,
                Mean = 0,
                StdDev = 0,
                NewMoney = 500_000,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "NewMoney arrives as pure brokerage cash"
            };

            var output = MonteCarloEngine.Run(parameters);

            // The withdrawal taken the year after NewMoney arrives should be nearly untaxed,
            // since the whole brokerage balance at that point is basis, not gain.
            double taxRateAfterArrival = output.Result.Runs[0].Years[1].TaxRate;
            Assert.InRange(taxRateAfterArrival, 0.0, 0.01);
        }

        [Fact]
        public void Run_TaxableUntouched_BeforeAge59AndAHalf()
        {
            var parameters = new SimulationParameters
            {
                Years = 10,
                Iterations = 5,
                Withdrawal = 20_000,
                Birthdate = Retire.AddYears(-40),
                RetirementDate = Retire,
                InitialTaxableBalance = 500_000,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 300_000,
                InitialBrokerageUnrealizedGain = 100_000,
                Mean = 0.05,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Age 40, withdrawal within Brokerage capacity"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.Runs[0].Years, yd => Assert.Equal(0, yd.TaxableWithdrawal));
            Assert.All(output.Result.Runs[0].Years, yd => Assert.False(yd.AgeEligible));
        }

        [Fact]
        public void Run_RothWithdrawal_CappedAtBasis_BeforeAge59AndAHalf()
        {
            var parameters = new SimulationParameters
            {
                Years = 10,
                Iterations = 5,
                Withdrawal = 20_000,
                Birthdate = Retire.AddYears(-40),
                RetirementDate = Retire,
                InitialTaxableBalance = 0,
                InitialRothBasis = 20_000,
                InitialRothUnrealizedGain = 200_000,
                InitialBrokerageBasis = 300_000,
                InitialBrokerageUnrealizedGain = 100_000,
                Mean = 0.05,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Age 40, Roth mostly gain"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.All(output.Result.Runs[0].Years, yd => Assert.True(yd.RothBalance >= 0));
        }

        [Fact]
        public void Run_Shortfall_Fails_WhenEligibleTotalBelowWithdrawal_DespiteLockedFundsRemaining()
        {
            var parameters = new SimulationParameters
            {
                Years = 10,
                Iterations = 5,
                Withdrawal = 60_000,
                Birthdate = Retire.AddYears(-40),
                RetirementDate = Retire,
                InitialTaxableBalance = 5_000_000,
                InitialRothBasis = 5_000,
                InitialRothUnrealizedGain = 5_000,
                InitialBrokerageBasis = 5_000,
                InitialBrokerageUnrealizedGain = 5_000,
                Mean = 0,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Age 40, huge locked Taxable balance, tiny accessible funds"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.True(output.Result.OutOfMoneyCount > 0);
        }

        [Fact]
        public void Run_WithdrawalsResumeFromAllBuckets_OnceAgeCrosses59AndAHalf()
        {
            var parameters = new SimulationParameters
            {
                Years = 5,
                Iterations = 3,
                Withdrawal = 20_000,
                Birthdate = Retire.AddYears(-59).AddMonths(-5),
                RetirementDate = Retire,
                InitialTaxableBalance = 500_000,
                InitialRothBasis = 50_000,
                InitialRothUnrealizedGain = 50_000,
                InitialBrokerageBasis = 300_000,
                InitialBrokerageUnrealizedGain = 100_000,
                Mean = 0.05,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Crosses age 59.5 mid-run"
            };

            var output = MonteCarloEngine.Run(parameters);

            var yearDetails = output.Result.Runs[0].Years;
            Assert.False(yearDetails[0].AgeEligible);
            Assert.Equal(0, yearDetails[0].TaxableWithdrawal);
            Assert.True(yearDetails[^1].AgeEligible);
            Assert.True(yearDetails[^1].TaxableWithdrawal > 0);
        }

        private static SimulationParameters ConversionParameters(bool enable) => new()
        {
            Years = 5,
            Iterations = 2,
            Withdrawal = 20_000,
            Birthdate = Retire.AddYears(-40),
            RetirementDate = Retire,
            InitialTaxableBalance = 500_000,
            InitialRothBasis = 0,
            InitialRothUnrealizedGain = 0,
            InitialBrokerageBasis = 300_000,
            InitialBrokerageUnrealizedGain = 100_000,
            Mean = 0,
            StdDev = 0,
            NewMoney = 0,
            YearNewMoney = 0,
            AnnualStandardDeduction = 0,
            EnableRothConversions = enable,
            ScenarioDescription = "Roth conversion scenario"
        };

        [Fact]
        public void Run_RothConversion_FillsTo22PercentBracket_WhenNoOrdinaryIncomeOrGains()
        {
            var parameters = ConversionParameters(enable: true);
            parameters.InitialBrokerageUnrealizedGain = 0; // all-basis Brokerage: its sales realize no gain

            var output = MonteCarloEngine.Run(parameters);
            var year0 = output.Result.Runs[0].Years[0];

            // Tax Deferred is locked (age 40), so all ordinary income comes from the conversion,
            // which should fill exactly to the (inflation-scaled) start of the 22% bracket.
            Assert.Equal(50_400, year0.RothConversionAmount, 3);
            Assert.True(year0.RothConversionTax > 0);
            Assert.Equal(year0.RothConversionTax, year0.OrdinaryTaxAmount, 6);
            Assert.Equal(0, year0.CapitalGainsTaxAmount, 9);
            Assert.Equal(0.12, year0.OrdinaryBracketRate);
            Assert.Equal(0, year0.AmountUntilNextBracket!.Value, 3);
            Assert.True(year0.RothBalance > 0);
            Assert.Equal(0, output.Result.OutOfMoneyCount);
        }

        [Fact]
        public void Run_RothConversion_StopsBeforePushingZeroRateGainsInto15Percent()
        {
            // Pro-rata, age 40: the $20,500 spending draw is all Brokerage (gain fraction 0.25), realizing
            // $5,125 of gain at 0%. The conversion stops where ordinary income plus those gains reaches the
            // 0% ceiling (49,450), rather than at the 22% line.
            var output = MonteCarloEngine.Run(ConversionParameters(enable: true));
            var year0 = output.Result.Runs[0].Years[0];
            double spendingSale = 20_000;

            Assert.Equal(49_450 - 0.25 * spendingSale, year0.RothConversionAmount, 3);
            Assert.Equal(year0.RothConversionTax, year0.OrdinaryTaxAmount, 6);
            // Only the conversion's funding sale lands above the 0% ceiling: its gain (a quarter of the sale)
            // is taxed at 15%, while the spending sale's gains stay untaxed.
            double fundingSale = year0.BrokerageWithdrawal - spendingSale;
            Assert.Equal(0.25 * 0.15 * fundingSale, year0.CapitalGainsTaxAmount, 6);
            Assert.Equal(0.12, year0.OrdinaryBracketRate);
            Assert.Equal(0.15, year0.CapitalGainsBracketRate);
        }

        [Fact]
        public void Run_RothConversion_None_WhenDisabled()
        {
            var output = MonteCarloEngine.Run(ConversionParameters(enable: false));

            Assert.All(output.Result.Runs[0].Years, yd => Assert.Equal(0, yd.RothConversionAmount));
            Assert.All(output.Result.Runs[0].Years, yd => Assert.Equal(0, yd.RothBalance));
        }

        [Fact]
        public void Run_RothConversion_None_WhenAlreadyIn22PercentBracket()
        {
            var parameters = ConversionParameters(enable: true);
            parameters.Birthdate = Retire.AddYears(-70);
            parameters.InitialBrokerageBasis = 0;
            parameters.InitialBrokerageUnrealizedGain = 0;
            parameters.InitialTaxableBalance = 2_000_000;
            parameters.Withdrawal = 120_000;

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.Runs[0].Years, yd => Assert.True(yd.OrdinaryBracketRate >= 0.22));
            Assert.All(output.Result.Runs[0].Years, yd => Assert.Equal(0, yd.RothConversionAmount));
        }

        [Fact]
        public void Run_RothConversion_CappedByBrokerageTaxCapacity()
        {
            var parameters = ConversionParameters(enable: true);
            parameters.InitialBrokerageBasis = 1_000;
            parameters.InitialBrokerageUnrealizedGain = 0;
            parameters.InitialRothBasis = 200_000;

            var output = MonteCarloEngine.Run(parameters);
            var year0 = output.Result.Runs[0].Years[0];

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.True(year0.RothConversionAmount > 0);
            Assert.True(year0.RothConversionAmount < 50_400);
            Assert.All(output.Result.Runs[0].Years, yd => Assert.True(yd.BrokerageBalance >= -0.01));
        }

        // Year 0 (inflation factor 1.0, no standard deduction): 12% ceiling = 50,400 gross.
        private const double TaxOptCeilingGross = 50_400;

        private static SimulationParameters TaxOptimizedParameters(int age, double withdrawal) => new()
        {
            Years = 3,
            Iterations = 1,
            Withdrawal = withdrawal,
            Birthdate = Retire.AddYears(-age),
            RetirementDate = Retire,
            InitialTaxableBalance = 1_000_000,
            InitialRothBasis = 100_000,
            InitialRothUnrealizedGain = 0,
            InitialBrokerageBasis = 300_000,
            InitialBrokerageUnrealizedGain = 0,
            Mean = 0,
            StdDev = 0,
            NewMoney = 0,
            YearNewMoney = 0,
            AnnualStandardDeduction = 0,
            WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
            ScenarioDescription = "Tax-optimized withdrawal order"
        };

        [Fact]
        public void TaxOptimized_SmallNeed_ComesEntirelyFromBrokerage_Untaxed()
        {
            // Gains first: Brokerage is drawn while its gains fit the 0% band. This Brokerage is all basis, so
            // the whole need comes from it tax-free and Tax Deferred is untouched.
            var year0 = MonteCarloEngine.Run(TaxOptimizedParameters(age: 70, withdrawal: 20_000)).Result.Runs[0].Years[0];

            Assert.Equal(0, year0.TaxableWithdrawal);
            Assert.Equal(20_000, year0.BrokerageWithdrawal, 6);
            Assert.Equal(0, year0.RothWithdrawal);
            Assert.Equal(0, year0.OrdinaryTaxAmount + year0.CapitalGainsTaxAmount, 9);
        }

        [Fact]
        public void TaxOptimized_GainHeavyBrokerage_ZeroRateGainsFirst_ThenBrokerageAt15Percent()
        {
            var parameters = TaxOptimizedParameters(age: 70, withdrawal: 80_000);
            parameters.InitialBrokerageBasis = 0;
            parameters.InitialBrokerageUnrealizedGain = 300_000;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            // Step 1 sells $49,450 of all-gain Brokerage at 0%, which leaves no room for
            // Tax Deferred below the 0% ceiling. The remaining $30,550 comes from Brokerage at 15%.
            double zeroRateSale = 49_450;
            double fifteenPercentSale = (80_000 - zeroRateSale) / 0.85;
            Assert.Equal(0, year0.TaxableWithdrawal);
            Assert.Equal(zeroRateSale + fifteenPercentSale, year0.BrokerageWithdrawal, 4);
            Assert.Equal(0.15 * fifteenPercentSale, year0.CapitalGainsTaxAmount, 4);
            Assert.Equal(0, year0.OrdinaryTaxAmount, 9);
            Assert.Equal(0.15, year0.CapitalGainsBracketRate);
            Assert.Equal(0.20, year0.NextCapitalGainsBracketRate);
            Assert.Equal(0, year0.RothWithdrawal);
        }

        [Fact]
        public void TaxOptimized_HarvestsLeftoverZeroRateRoom()
        {
            // Age 45 (Tax Deferred locked), no conversions: a small draw leaves most of the 0% band unused, so
            // the rest is harvested - stepping up basis without moving cash.
            var parameters = TaxOptimizedParameters(age: 45, withdrawal: 10_000);
            parameters.InitialBrokerageBasis = 0;
            parameters.InitialBrokerageUnrealizedGain = 300_000;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            Assert.Equal(49_450 - 10_000, year0.HarvestedGains, 4);
            Assert.Equal(0, year0.CapitalGainsTaxAmount, 9);
            Assert.Equal(0, year0.CapitalGainsBracketRate);
            Assert.Equal(0, year0.AmountUntilNextCapitalGainsBracket!.Value, 4);
            Assert.Equal(0.15, year0.NextCapitalGainsBracketRate);
        }

        [Fact]
        public void ProRata_DoesNotHarvest()
        {
            var parameters = TaxOptimizedParameters(age: 45, withdrawal: 10_000);
            parameters.InitialBrokerageBasis = 0;
            parameters.InitialBrokerageUnrealizedGain = 300_000;
            parameters.WithdrawalStrategy = WithdrawalStrategy.ProRata;

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.Runs[0].Years, yd => Assert.Equal(0, yd.HarvestedGains));
        }

        [Fact]
        public void TaxOptimized_AfterBrokerageExhausted_TaxDeferredGoesAbove12Percent_BeforeRoth()
        {
            var parameters = TaxOptimizedParameters(age: 70, withdrawal: 80_000);
            parameters.InitialBrokerageBasis = 10_000;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            Assert.Equal(10_000, year0.BrokerageWithdrawal, 3);
            Assert.Equal(0, year0.BrokerageBalance, 3);
            Assert.True(year0.TaxableWithdrawal > TaxOptCeilingGross);
            Assert.Equal(0.22, year0.OrdinaryBracketRate);
            Assert.Equal(0, year0.RothWithdrawal);
        }

        [Fact]
        public void TaxOptimized_Roth_OnlyAfterTaxDeferredAndBrokerageExhausted()
        {
            var parameters = TaxOptimizedParameters(age: 70, withdrawal: 80_000);
            parameters.InitialTaxableBalance = 30_000;
            parameters.InitialBrokerageBasis = 10_000;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            Assert.Equal(30_000, year0.TaxableWithdrawal, 3);
            Assert.Equal(10_000, year0.BrokerageWithdrawal, 3);
            Assert.True(year0.RothWithdrawal > 0);
        }

        [Fact]
        public void TaxOptimized_DoesNotFail_WhileTaxDeferredCanStillCoverSpending()
        {
            // Regression: the 12% ceiling used to be a hard cap, so once Brokerage and Roth ran dry the
            // run failed even with plenty of Tax Deferred money left.
            var parameters = TaxOptimizedParameters(age: 70, withdrawal: 120_000);
            parameters.Years = 5;
            parameters.InitialBrokerageBasis = 0;
            parameters.InitialRothBasis = 0;

            var output = MonteCarloEngine.Run(parameters);

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.All(output.Result.Runs[0].Years, yd => Assert.True(yd.TaxableWithdrawal > TaxOptCeilingGross));
        }

        [Fact]
        public void TaxOptimized_BeforeAge59AndAHalf_UsesBrokerageBeforeRoth()
        {
            var output = MonteCarloEngine.Run(TaxOptimizedParameters(age: 40, withdrawal: 20_000));

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.All(output.Result.Runs[0].Years, yd => Assert.Equal(0, yd.TaxableWithdrawal));
            Assert.All(output.Result.Runs[0].Years, yd => Assert.True(yd.BrokerageWithdrawal > 0));
            Assert.All(output.Result.Runs[0].Years, yd => Assert.Equal(0, yd.RothWithdrawal));
        }

        [Fact]
        public void TaxOptimized_WithConversions_BelowFillYear_FillsExactlyTo22Percent()
        {
            var parameters = TaxOptimizedParameters(age: 70, withdrawal: 20_000);
            parameters.EnableRothConversions = true;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            Assert.True(year0.RothConversionAmount > 0);
            Assert.Equal(TaxOptCeilingGross, year0.TaxableWithdrawal + year0.RothConversionAmount, 3);
            Assert.Equal(0.12, year0.OrdinaryBracketRate);
            Assert.Equal(0, year0.AmountUntilNextBracket!.Value, 3);
        }

        // Social Security, year 0 (inflation factor 1.0): 12 payments of $2,500 = $30,000, 85% taxable = $25,500;
        // standard deduction 16,000, so $9,500 is taxed at 10% = $950 and the net benefit is $29,050.
        private const double SsTaxableYear0 = 25_500;
        private const double SsTaxYear0 = 950;
        private const double SsNetYear0 = 29_050;
        private const double StdDedYear0 = 16_000;

        private static SimulationParameters SocialSecurityParameters(int age, double withdrawal) => new()
        {
            Years = 3,
            Iterations = 1,
            Withdrawal = withdrawal,
            Birthdate = Retire.AddYears(-age),
            RetirementDate = Retire,
            InitialTaxableBalance = 0,
            InitialRothBasis = 0,
            InitialRothUnrealizedGain = 0,
            InitialBrokerageBasis = 0,
            InitialBrokerageUnrealizedGain = 0,
            Mean = 0,
            StdDev = 0,
            NewMoney = 0,
            YearNewMoney = 0,
            SocialSecurityStartDate = Retire,
            SocialSecurityMonthlyAmount = 2_500, // 12 payments in a full year = 30,000
            AnnualStandardDeduction = 16_000,
            WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
            ScenarioDescription = "Social Security as income"
        };

        [Fact]
        public void SocialSecurity_TaxedAt85Percent_AndNetReducesPortfolioNeed()
        {
            var parameters = SocialSecurityParameters(age: 70, withdrawal: 50_000);
            parameters.InitialBrokerageBasis = 500_000; // all basis, so the Brokerage draw is untaxed and gross == net

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            Assert.Equal(30_000, year0.SocialSecurityIncome);
            Assert.Equal(SsTaxYear0, year0.SocialSecurityTax, 6);
            Assert.Equal(50_000 - SsNetYear0, year0.BrokerageWithdrawal, 6);
        }

        [Fact]
        public void SocialSecurity_StacksFirst_TaxDeferredTaxedAboveIt()
        {
            var parameters = SocialSecurityParameters(age: 70, withdrawal: 50_000);
            parameters.InitialTaxableBalance = 1_000_000;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            // Need 20,950 net. SS already sits $9,500 into the 10% bracket (top 12,400), leaving 2,900 gross
            // (2,610 net) at 10%; the other 18,340 net is at 12% -> 20,840.91 gross. Total gross 23,740.91.
            double expectedGross = 2_900 + 18_340 / 0.88;
            Assert.Equal(expectedGross, year0.TaxableWithdrawal, 4);
            // Ordinary tax on all income: 1,240 (10% bracket) + 20,840.91 * 12%.
            Assert.Equal(1_240 + (18_340 / 0.88) * 0.12, year0.OrdinaryTaxAmount, 4);
            Assert.Equal(0.12, year0.OrdinaryBracketRate);
        }

        [Fact]
        public void SocialSecurity_ShrinksTaxOptimized12PercentFill()
        {
            // All-basis Brokerage covers the spending tax-free (realizing no gains) and pays the conversion's tax;
            // Tax Deferred draws plus the conversion fill to the 22% line, less the taxable SS already there.
            var parameters = SocialSecurityParameters(age: 70, withdrawal: 50_000);
            parameters.InitialTaxableBalance = 1_000_000;
            parameters.InitialBrokerageBasis = 500_000;
            parameters.EnableRothConversions = true;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            Assert.Equal(StdDedYear0 + TaxOptCeilingGross - SsTaxableYear0, year0.TaxableWithdrawal + year0.RothConversionAmount, 3);
            Assert.Equal(0.12, year0.OrdinaryBracketRate);
            Assert.Equal(0, year0.AmountUntilNextBracket!.Value, 3);
        }

        [Fact]
        public void SocialSecurity_ShrinksZeroRateGainRoom()
        {
            // Taxable SS sits in the ordinary stack beneath gains, so the gains taxed at 0% are the 0% ceiling
            // (deduction + 49,450, inflated) less the taxable SS; the rest of the all-gain sale is taxed at 15%.
            var parameters = SocialSecurityParameters(age: 70, withdrawal: 150_000);
            parameters.InitialBrokerageUnrealizedGain = 1_000_000;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            double zeroRateGains = StdDedYear0 + 49_450 - SsTaxableYear0;
            Assert.Equal(0, year0.TaxableWithdrawal);
            Assert.Equal(0.15 * (year0.BrokerageWithdrawal - zeroRateGains), year0.CapitalGainsTaxAmount, 4);
            Assert.Equal(SsTaxYear0, year0.OrdinaryTaxAmount, 6);
        }

        [Fact]
        public void SocialSecurity_ShrinksRothConversionRoom()
        {
            var parameters = SocialSecurityParameters(age: 40, withdrawal: 50_000);
            parameters.InitialTaxableBalance = 500_000;
            parameters.InitialBrokerageBasis = 500_000;
            parameters.EnableRothConversions = true;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            Assert.Equal(0, year0.TaxableWithdrawal);
            Assert.Equal(StdDedYear0 + TaxOptCeilingGross - SsTaxableYear0, year0.RothConversionAmount, 3);
        }

        [Fact]
        public void SocialSecurity_SurplusDepositedToBrokerage()
        {
            var parameters = SocialSecurityParameters(age: 70, withdrawal: 10_000);
            parameters.InitialBrokerageBasis = 100_000;

            var year0 = MonteCarloEngine.Run(parameters).Result.Runs[0].Years[0];

            Assert.Equal(0, year0.BrokerageWithdrawal);
            Assert.Equal(100_000 + (SsNetYear0 - 10_000), year0.BrokerageBalance, 6);
        }

        [Fact]
        public void SocialSecurity_NoLongerDepositedIntoTaxDeferred()
        {
            var parameters = SocialSecurityParameters(age: 40, withdrawal: 50_000);
            parameters.InitialTaxableBalance = 500_000;
            parameters.InitialBrokerageBasis = 500_000;

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.Runs[0].Years, yd => Assert.Equal(500_000, yd.TaxableBalance, 6));
        }

        [Fact]
        public void TaxOptimized_Shortfall_Fails_WhenEligibleBucketsCannotCoverNeed()
        {
            var parameters = TaxOptimizedParameters(age: 40, withdrawal: 60_000);
            parameters.InitialBrokerageBasis = 5_000;
            parameters.InitialRothBasis = 5_000;

            var output = MonteCarloEngine.Run(parameters);

            Assert.True(output.Result.OutOfMoneyCount > 0);
        }
    }
}
