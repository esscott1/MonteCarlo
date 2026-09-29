namespace MonteCarloSimulation.Core.Tests
{
    public class MonteCarloEngineTests
    {
        [Fact]
        public void Run_AllIterationsSucceed_WhenReturnsExceedWithdrawals()
        {
            var parameters = new SimulationParameters
            {
                Years = 20,
                Iterations = 50,
                Withdrawal = 20_000,
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70),
                InitialTaxableBalance = 500_000,
                InitialRothBasis = 250_000,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 150_000,
                InitialBrokerageUnrealizedGain = 100_000,
                Mean = 0.07,
                StdDev = 0, // every run gets exactly Mean as its return every year - fully deterministic
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Deterministic 7% return, small withdrawal"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.Equal(parameters.Iterations, output.Result.EndingBalances.Count);
        }

        [Fact]
        public void Run_AllIterationsFail_WhenWithdrawalExceedsBalance()
        {
            var parameters = new SimulationParameters
            {
                Years = 20,
                Iterations = 50,
                Withdrawal = 500_000,
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70),
                InitialTaxableBalance = 300_000,
                InitialRothBasis = 150_000,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 90_000,
                InitialBrokerageUnrealizedGain = 60_000,
                Mean = 0,
                StdDev = 0, // every run gets exactly 0% return every year - fully deterministic
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
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
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70),
                InitialTaxableBalance = 700_000,
                InitialRothBasis = 350_000,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 210_000,
                InitialBrokerageUnrealizedGain = 140_000,
                Mean = 0.0807,
                StdDev = 0.1915, // "Last 95 years of S&P" preset volatility
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
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
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70),
                InitialTaxableBalance = 700_000,
                InitialRothBasis = 350_000,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 210_000,
                InitialBrokerageUnrealizedGain = 140_000,
                Mean = 0.0807,
                StdDev = 0.1915, // "Last 95 years of S&P" preset volatility
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
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
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70),
                InitialTaxableBalance = 0,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 500_000,
                InitialBrokerageUnrealizedGain = 0,
                Mean = 0,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "All-basis brokerage withdrawal"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.AverageTaxRates, rate => Assert.InRange(rate, 0.0, 0.01));
        }

        [Fact]
        public void Run_BrokerageWithdrawal_TaxedNearLtcgRate_WhenMostlyGain()
        {
            var parameters = new SimulationParameters
            {
                Years = 10,
                Iterations = 20,
                Withdrawal = 20_000,
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70),
                InitialTaxableBalance = 0,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 0,
                InitialBrokerageUnrealizedGain = 500_000,
                Mean = 0,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "All-gain brokerage withdrawal"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.AverageTaxRates, rate => Assert.InRange(rate, 0.15, 0.20));
        }

        [Fact]
        public void Run_BrokerageTaxRate_IsHigher_WithMoreEmbeddedGain()
        {
            SimulationParameters BuildParameters(double basis, double gain) => new()
            {
                Years = 10,
                Iterations = 20,
                Withdrawal = 20_000,
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70),
                InitialTaxableBalance = 0,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = basis,
                InitialBrokerageUnrealizedGain = gain,
                Mean = 0,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Basis-vs-gain contrast"
            };

            var highBasisOutput = MonteCarloEngine.Run(BuildParameters(basis: 450_000, gain: 50_000));
            var highGainOutput = MonteCarloEngine.Run(BuildParameters(basis: 50_000, gain: 450_000));

            double highBasisAvgTaxRate = highBasisOutput.Result.AverageTaxRates.Average();
            double highGainAvgTaxRate = highGainOutput.Result.AverageTaxRates.Average();

            Assert.True(highGainAvgTaxRate - highBasisAvgTaxRate > 0.10);
        }

        [Fact]
        public void Run_NewMoney_LandsInBrokerageAsBasis_NotGain()
        {
            var parameters = new SimulationParameters
            {
                Years = 5,
                Iterations = 10,
                Withdrawal = 20_000,
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70),
                InitialTaxableBalance = 0,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 30_000,
                InitialBrokerageUnrealizedGain = 0,
                Mean = 0,
                StdDev = 0,
                NewMoney = 500_000,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "NewMoney arrives as pure brokerage cash"
            };

            var output = MonteCarloEngine.Run(parameters);

            // The withdrawal taken the year after NewMoney arrives should be nearly untaxed,
            // since the whole brokerage balance at that point is basis, not gain.
            double taxRateAfterArrival = output.Result.RunDetails[0][1].TaxRate;
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
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-40),
                InitialTaxableBalance = 500_000,
                InitialRothBasis = 0,
                InitialRothUnrealizedGain = 0,
                InitialBrokerageBasis = 300_000,
                InitialBrokerageUnrealizedGain = 100_000,
                Mean = 0.05,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Age 40, withdrawal within Brokerage capacity"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.RunDetails[0], yd => Assert.Equal(0, yd.TaxableWithdrawal));
            Assert.All(output.Result.RunDetails[0], yd => Assert.False(yd.AgeEligible));
        }

        [Fact]
        public void Run_RothWithdrawal_CappedAtBasis_BeforeAge59AndAHalf()
        {
            var parameters = new SimulationParameters
            {
                Years = 10,
                Iterations = 5,
                Withdrawal = 20_000,
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-40),
                InitialTaxableBalance = 0,
                InitialRothBasis = 20_000,
                InitialRothUnrealizedGain = 200_000,
                InitialBrokerageBasis = 300_000,
                InitialBrokerageUnrealizedGain = 100_000,
                Mean = 0.05,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Age 40, Roth mostly gain"
            };

            var output = MonteCarloEngine.Run(parameters);

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.All(output.Result.RunDetails[0], yd => Assert.True(yd.RothBalance >= 0));
        }

        [Fact]
        public void Run_Shortfall_Fails_WhenEligibleTotalBelowWithdrawal_DespiteLockedFundsRemaining()
        {
            var parameters = new SimulationParameters
            {
                Years = 10,
                Iterations = 5,
                Withdrawal = 60_000,
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-40),
                InitialTaxableBalance = 5_000_000,
                InitialRothBasis = 5_000,
                InitialRothUnrealizedGain = 5_000,
                InitialBrokerageBasis = 5_000,
                InitialBrokerageUnrealizedGain = 5_000,
                Mean = 0,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
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
                Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-59).AddMonths(-5),
                InitialTaxableBalance = 500_000,
                InitialRothBasis = 50_000,
                InitialRothUnrealizedGain = 50_000,
                InitialBrokerageBasis = 300_000,
                InitialBrokerageUnrealizedGain = 100_000,
                Mean = 0.05,
                StdDev = 0,
                NewMoney = 0,
                YearNewMoney = 0,
                SocialSecurityYearsUntilStart = 0,
                SocialSecurityAnnualAmount = 0,
                AnnualStandardDeduction = 0,
                ScenarioDescription = "Crosses age 59.5 mid-run"
            };

            var output = MonteCarloEngine.Run(parameters);

            var yearDetails = output.Result.RunDetails[0];
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
            Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-40),
            InitialTaxableBalance = 500_000,
            InitialRothBasis = 0,
            InitialRothUnrealizedGain = 0,
            InitialBrokerageBasis = 300_000,
            InitialBrokerageUnrealizedGain = 100_000,
            Mean = 0,
            StdDev = 0,
            NewMoney = 0,
            YearNewMoney = 0,
            SocialSecurityYearsUntilStart = 0,
            SocialSecurityAnnualAmount = 0,
            AnnualStandardDeduction = 0,
            EnableRothConversions = enable,
            ScenarioDescription = "Roth conversion scenario"
        };

        [Fact]
        public void Run_RothConversion_FillsTo22PercentBracket_WhenNoOrdinaryIncome()
        {
            var output = MonteCarloEngine.Run(ConversionParameters(enable: true));
            var year0 = output.Result.RunDetails[0][0];

            // Tax Deferred is locked (age 40), so all ordinary income comes from the conversion,
            // which should fill exactly to the (inflation-scaled) start of the 22% bracket.
            Assert.Equal(50_400 * 1.025, year0.RothConversionAmount, 3);
            Assert.True(year0.RothConversionTax > 0);
            Assert.Equal(year0.RothConversionTax, year0.OrdinaryTaxAmount, 6);
            Assert.Equal(0.12, year0.OrdinaryBracketRate);
            Assert.Equal(0, year0.AmountUntilNextBracket!.Value, 3);
            Assert.True(year0.RothBalance > 0);
            Assert.Equal(0, output.Result.OutOfMoneyCount);
        }

        [Fact]
        public void Run_RothConversion_None_WhenDisabled()
        {
            var output = MonteCarloEngine.Run(ConversionParameters(enable: false));

            Assert.All(output.Result.RunDetails[0], yd => Assert.Equal(0, yd.RothConversionAmount));
            Assert.All(output.Result.RunDetails[0], yd => Assert.Equal(0, yd.RothBalance));
        }

        [Fact]
        public void Run_RothConversion_None_WhenAlreadyIn22PercentBracket()
        {
            var parameters = ConversionParameters(enable: true);
            parameters.Birthdate = DateOnly.FromDateTime(DateTime.Today).AddYears(-70);
            parameters.InitialBrokerageBasis = 0;
            parameters.InitialBrokerageUnrealizedGain = 0;
            parameters.InitialTaxableBalance = 2_000_000;
            parameters.Withdrawal = 120_000;

            var output = MonteCarloEngine.Run(parameters);

            Assert.All(output.Result.RunDetails[0], yd => Assert.True(yd.OrdinaryBracketRate >= 0.22));
            Assert.All(output.Result.RunDetails[0], yd => Assert.Equal(0, yd.RothConversionAmount));
        }

        [Fact]
        public void Run_RothConversion_CappedByBrokerageTaxCapacity()
        {
            var parameters = ConversionParameters(enable: true);
            parameters.InitialBrokerageBasis = 1_000;
            parameters.InitialBrokerageUnrealizedGain = 0;
            parameters.InitialRothBasis = 200_000;

            var output = MonteCarloEngine.Run(parameters);
            var year0 = output.Result.RunDetails[0][0];

            Assert.Equal(0, output.Result.OutOfMoneyCount);
            Assert.True(year0.RothConversionAmount > 0);
            Assert.True(year0.RothConversionAmount < 50_400 * 1.025);
            Assert.All(output.Result.RunDetails[0], yd => Assert.True(yd.BrokerageBalance >= -0.01));
        }
    }
}
