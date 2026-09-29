namespace MonteCarloSimulation.Core
{
    // Simulates one run (one Monte Carlo iteration) year by year until it ends or fails.
    internal static class RunSimulator
    {
        public static RunSummary Simulate(SimulationParameters parameters, IWithdrawalStrategy strategy, double ageAtStartYears, Random random)
        {
            var accounts = Accounts.FromParameters(parameters);
            var indexes = new InflationIndexes(parameters);
            var years = new List<RunYearDetail>(parameters.Years);

            for (int year = 0; year < parameters.Years; year++)
            {
                indexes.Advance(year);
                double rate = DrawReturn(parameters.Mean, parameters.StdDev, random);

                var (detail, failed) = SimulateYear(parameters, strategy, accounts, indexes, year, rate, ageAtStartYears + year);
                years.Add(detail);

                // A run fails the instant any bucket (or the total) goes negative, or the eligible buckets
                // can't cover the year's need - even while age-locked money keeps the total positive.
                if (failed)
                    return new RunSummary { Years = years, FailureYear = year };
            }

            return new RunSummary { Years = years, FailureYear = null };
        }

        private static (RunYearDetail Detail, bool Failed) SimulateYear(
            SimulationParameters parameters, IWithdrawalStrategy strategy, Accounts accounts,
            InflationIndexes indexes, int year, double rate, double ageInYear)
        {
            var taxYear = indexes.TaxYear;
            double spending = indexes.Withdrawal;
            var ss = SocialSecurityYear.Compute(indexes.SocialSecurity, taxYear, spending);

            // Start-of-year (prior year-end) Tax Deferred balance, for the "% of balance" figure
            double taxableStartOfYear = accounts.Taxable;

            // All three buckets grow by the same rate, then this year's withdrawal is taken
            accounts.ApplyReturn(rate);

            bool ageEligible = ageInYear >= 59.5;
            var plan = strategy.Plan(new WithdrawalContext(
                ss.Need, ss.Taxable, taxYear,
                accounts.EligibleTaxable(ageEligible), accounts.Brokerage, accounts.BrokerageGainFraction, accounts.EligibleRoth(ageEligible)));

            accounts.WithdrawTaxable(plan.GrossTaxable);
            accounts.SellBrokerage(plan.GrossBrokerage);
            accounts.WithdrawRoth(plan.Roth);

            var conversion = parameters.EnableRothConversions
                ? RothConversion.Apply(accounts, taxYear, ss.Taxable + plan.GrossTaxable)
                : RothConversion.None;

            // The year's reported taxes and Brokerage sales include the conversion and the sale funding its tax
            double ordinaryTax = plan.TaxableTax + ss.Tax + conversion.Tax;
            double capitalGainsTax = plan.CapitalGainsTax + conversion.CapitalGainsTax;
            double brokerageWithdrawal = plan.GrossBrokerage + conversion.TaxSale;

            // Blended effective tax rate across all buckets (Roth always contributes 0)
            double totalGross = plan.GrossTaxable + brokerageWithdrawal + plan.Roth + ss.Gross;
            double taxRate = totalGross > 0 ? (ordinaryTax + capitalGainsTax) / totalGross : 0;

            var (bracketRate, amountUntilNextBracket, nextBracketRate) = taxYear.BracketRoom(ss.Taxable + plan.GrossTaxable + conversion.Amount);

            // New money (e.g. an inheritance) arrives as after-tax cash in its year; so does any Social
            // Security beyond this year's spending
            if (year == parameters.YearNewMoney)
                accounts.DepositBrokerageCash(parameters.NewMoney);
            accounts.DepositBrokerageCash(ss.Surplus);

            // Reported return, backed out of the year-end balances
            double returnAmount = accounts.Total
                - (accounts.Taxable / (1 + rate) + accounts.Brokerage / (1 + rate) + accounts.Roth / (1 + rate));

            var detail = new RunYearDetail
            {
                Year = year,
                RateOfReturn = rate,
                ReturnAmount = returnAmount,
                Withdrawal = spending,
                TaxableWithdrawal = plan.GrossTaxable,
                BrokerageWithdrawal = brokerageWithdrawal,
                RothWithdrawal = plan.Roth,
                TaxRate = taxRate,
                Balance = accounts.Total,
                TaxableBalance = accounts.Taxable,
                BrokerageBalance = accounts.Brokerage,
                RothBalance = accounts.Roth,
                OrdinaryTaxAmount = ordinaryTax,
                CapitalGainsTaxAmount = capitalGainsTax,
                OrdinaryBracketRate = bracketRate,
                AmountUntilNextBracket = amountUntilNextBracket,
                NextBracketRate = nextBracketRate,
                AgeEligible = ageEligible,
                RothConversionAmount = conversion.Amount,
                RothConversionTax = conversion.Tax,
                AgeInYear = ageInYear,
                TaxableWithdrawalPercentOfBalance = taxableStartOfYear > 0 ? plan.GrossTaxable / taxableStartOfYear : 0,
                SocialSecurityIncome = ss.Gross,
                SocialSecurityTax = ss.Tax
            };

            return (detail, accounts.AnyNegative || plan.IsShortfall);
        }

        // Normally distributed annual return via the Box-Muller transform. One draw per simulated year.
        private static double DrawReturn(double mean, double standardDeviation, Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return mean + standardDeviation * randStdNormal;
        }
    }
}
