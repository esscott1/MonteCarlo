namespace MonteCarloSimulation.Core.Tests
{
    // An annuity the household already owns: income like Social Security, its basis returned tax-free over the first
    // 312 payments (non-qualified), nothing tax-free when it's inside a tax-deferred account (Basis 0).
    public class AnnuityTests
    {
        private const double Monthly = 2_600;
        private const double Basis = 400_000;
        private const double Excluded = Basis / 312; // 1,282.05 of each payment while the basis is recovered

        private static SimulationParameters Parameters(DateOnly retire, int years, Annuity? annuity) => new()
        {
            Years = years,
            Iterations = 1,
            Birthdate = new DateOnly(1960, 3, 1),
            RetirementDate = retire,
            SocialSecurityStartDate = retire.AddYears(100),
            AnnualStandardDeduction = 16_000,
            WithdrawalStrategy = WithdrawalStrategy.TaxOptimized,
            Annuity = annuity,
            ScenarioDescription = "annuity test"
        };

        private static IReadOnlyList<RetirementYear> Timeline(DateOnly retire, int years, Annuity annuity) =>
            RetirementTimeline.Build(Parameters(retire, years, annuity));

        private static RetirementYear In(IReadOnlyList<RetirementYear> timeline, int calendarYear) =>
            timeline.Single(y => y.CalendarYear == calendarYear);

        // --- The timeline: payments, gross income and its taxable part ---

        [Fact]
        public void Spia_PaysFromRetirement_AndReturnsBasisTaxFree()
        {
            var retire = new DateOnly(2027, 1, 1);
            var timeline = Timeline(retire, 30, new Annuity { MonthlyPayment = Monthly, FirstPayment = retire, Basis = Basis });

            var first = In(timeline, 2027);
            Assert.Equal(12, first.AnnuityPayments);
            Assert.Equal(12 * Monthly, first.AnnuityIncome, 9);
            Assert.Equal(12 * (Monthly - Excluded), first.AnnuityTaxable, 9);
            // A fixed payment: no inflation, years later
            Assert.Equal(12 * Monthly, In(timeline, 2040).AnnuityIncome, 9);
        }

        [Fact]
        public void Dia_StartsOnItsDate_WithAPartialFirstYear()
        {
            var timeline = Timeline(new DateOnly(2027, 1, 1), 30,
                new Annuity { MonthlyPayment = Monthly, FirstPayment = new DateOnly(2031, 4, 15), Basis = Basis });

            Assert.Equal(0, In(timeline, 2030).AnnuityPayments);
            Assert.Equal(0, In(timeline, 2030).AnnuityIncome);
            Assert.Equal(9, In(timeline, 2031).AnnuityPayments); // April 15 through December 15
            Assert.Equal(9 * Monthly, In(timeline, 2031).AnnuityIncome, 9);
            Assert.Equal(12, In(timeline, 2032).AnnuityPayments);
        }

        [Fact]
        public void Cola_RaisesThePaymentEachJanuary_AfterTheFirstPaymentsYear()
        {
            var timeline = Timeline(new DateOnly(2027, 1, 1), 30,
                new Annuity { MonthlyPayment = Monthly, FirstPayment = new DateOnly(2031, 4, 15), Basis = Basis, AnnualIncrease = 0.02 });

            Assert.Equal(9 * Monthly, In(timeline, 2031).AnnuityIncome, 9);
            Assert.Equal(12 * Monthly * 1.02, In(timeline, 2032).AnnuityIncome, 9);
            Assert.Equal(12 * Monthly * 1.02 * 1.02, In(timeline, 2033).AnnuityIncome, 6);
            // The tax-free part stays Basis / 312 a payment as the payment grows
            Assert.Equal(12 * (Monthly * 1.02 - Excluded), In(timeline, 2032).AnnuityTaxable, 9);
        }

        [Fact]
        public void AfterPayment312_EveryPaymentIsTaxable()
        {
            // First payment July 1, 2027: 6 payments in 2027, so payment 312 is June 1, 2053
            var retire = new DateOnly(2027, 7, 1);
            var timeline = Timeline(retire, 30, new Annuity { MonthlyPayment = Monthly, FirstPayment = retire, Basis = Basis });

            Assert.Equal(6, In(timeline, 2027).AnnuityPayments);
            Assert.Equal(12 * (Monthly - Excluded), In(timeline, 2052).AnnuityTaxable, 9);
            Assert.Equal(12 * Monthly - 6 * Excluded, In(timeline, 2053).AnnuityTaxable, 9); // 6 still recovering
            Assert.Equal(12 * Monthly, In(timeline, 2054).AnnuityTaxable, 9);
            // The whole basis comes back, no more
            Assert.Equal(Basis, timeline.Sum(y => y.AnnuityIncome - y.AnnuityTaxable), 6);
        }

        [Fact]
        public void InATaxDeferredAccount_EveryPaymentIsTaxable()
        {
            var retire = new DateOnly(2027, 1, 1);
            var timeline = Timeline(retire, 30, new Annuity { MonthlyPayment = Monthly, FirstPayment = retire, Basis = 0 });

            Assert.All(timeline, y => Assert.Equal(y.AnnuityIncome, y.AnnuityTaxable));
            Assert.Equal(12 * Monthly, In(timeline, 2027).AnnuityTaxable, 9);
        }

        [Fact]
        public void PaymentsBeforeRetirement_UseUpTheBasisRecovery()
        {
            // Paying since January 2010: 240 payments before 2030, so 72 recover basis inside the window
            var timeline = Timeline(new DateOnly(2030, 1, 1), 10,
                new Annuity { MonthlyPayment = Monthly, FirstPayment = new DateOnly(2010, 1, 1), Basis = Basis });

            Assert.Equal(12 * (Monthly - Excluded), In(timeline, 2035).AnnuityTaxable, 9);
            Assert.Equal(12 * Monthly, In(timeline, 2036).AnnuityTaxable, 9);
        }

        [Fact]
        public void NoAnnuity_AddsNothing()
        {
            var timeline = RetirementTimeline.Build(Parameters(new DateOnly(2027, 1, 1), 5, null));

            Assert.All(timeline, y =>
            {
                Assert.Equal(0, y.AnnuityPayments);
                Assert.Equal(0, y.AnnuityIncome);
                Assert.Equal(0, y.AnnuityTaxable);
            });
        }

        // --- One year's annuity income ---

        [Fact]
        public void AnnuityYear_IsTaxedOnTopOfSocialSecurity_AndNetsAgainstTheNeed()
        {
            // 25,500 of taxable Social Security already uses the 16,000 deduction and 9,500 of the 10% bracket (to
            // 12,400): 15,000 more taxable is 2,900 at 10% + 12,100 at 12% = 1,742. Net 20,000 - 1,742 = 18,258.
            var taxYear = Core.TaxYear.For(FilingStatus.Single, 16_000, 1.0);

            var shortOfNeed = AnnuityYear.Compute(20_000, 15_000, taxYear, baseIncome: 25_500, need: 50_000);
            var aboveNeed = AnnuityYear.Compute(20_000, 15_000, taxYear, baseIncome: 25_500, need: 10_000);

            Assert.Equal(1_742, shortOfNeed.Tax, 6);
            Assert.Equal(50_000 - 18_258, shortOfNeed.Need, 6);
            Assert.Equal(0, shortOfNeed.Surplus);
            Assert.Equal(0, aboveNeed.Need);
            Assert.Equal(18_258 - 10_000, aboveNeed.Surplus, 6);
            Assert.Equal(AnnuityYear.None(50_000), AnnuityYear.Compute(0, 0, taxYear, 25_500, 50_000));
        }

        // --- Through the engine ---

        [Fact]
        public void Engine_QualifiedAnnuity_IsOrdinaryIncome_AndItsSurplusIsSaved()
        {
            // 2026, inflation factor 1: 31,200 a year, all ordinary income. 15,200 above the 16,000 deduction:
            // 12,400 at 10% + 2,800 at 12% = 1,576 tax. Net 29,624 against 20,000 of spending: 9,624 saved.
            var retire = new DateOnly(2026, 1, 1);
            var parameters = Parameters(retire, 3, new Annuity { MonthlyPayment = Monthly, FirstPayment = retire, Basis = 0 });
            parameters.Withdrawal = 20_000;

            var year0 = MonteCarloEngine.Run(parameters, new Random(0)).Result.Runs[0].Years[0];

            Assert.Equal(31_200, year0.AnnuityIncome, 9);
            Assert.Equal(31_200, year0.AnnuityTaxableIncome, 9);
            Assert.Equal(1_576, year0.AnnuityTax, 6);
            Assert.Equal(1_576, year0.OrdinaryTaxAmount, 6);
            Assert.Equal(31_200, year0.Magi, 6);
            Assert.Equal(12, year0.AnnuityPayments);
            Assert.Equal(9_624, year0.BrokerageBalance, 6);
            Assert.Equal(0, year0.TaxableWithdrawal + year0.BrokerageWithdrawal + year0.RothWithdrawal);
        }

        [Fact]
        public void Engine_NonQualifiedAnnuity_TaxesOnlyThePartAboveBasis()
        {
            // 12 * (2,600 - 1,282.05) = 15,815.38 taxable sits under the 16,000 deduction: no tax, all 31,200 net
            var retire = new DateOnly(2026, 1, 1);
            var parameters = Parameters(retire, 3, new Annuity { MonthlyPayment = Monthly, FirstPayment = retire, Basis = Basis });
            parameters.Withdrawal = 20_000;
            parameters.InitialTaxableBalance = 500_000;

            var year0 = MonteCarloEngine.Run(parameters, new Random(0)).Result.Runs[0].Years[0];

            Assert.Equal(12 * (Monthly - Excluded), year0.AnnuityTaxableIncome, 9);
            Assert.Equal(0, year0.AnnuityTax);
            Assert.Equal(11_200, year0.BrokerageBalance, 6);
            Assert.Equal(12 * (Monthly - Excluded), year0.Magi, 6);
        }

        [Fact]
        public void Engine_TaxDeferredDraws_StackOnTopOfTheAnnuity()
        {
            // A qualified annuity's 31,200 fills the deduction and the 10% bracket and reaches 2,800 into 12%, so
            // every Tax Deferred dollar drawn for the remaining spending is taxed at 12%.
            var retire = new DateOnly(2026, 1, 1);
            var parameters = Parameters(retire, 3, new Annuity { MonthlyPayment = Monthly, FirstPayment = retire, Basis = 0 });
            parameters.Withdrawal = 60_000;
            parameters.InitialTaxableBalance = 1_000_000;

            var year0 = MonteCarloEngine.Run(parameters, new Random(0)).Result.Runs[0].Years[0];

            double need = 60_000 - (31_200 - 1_576);
            Assert.Equal(need / 0.88, year0.TaxableWithdrawal, 4);
            Assert.Equal(need / 0.88 * 0.12, year0.TaxDeferredWithdrawalTax, 4);
        }
    }
}
