namespace MonteCarloSimulation.Core
{
    // Simulates one run (one Monte Carlo iteration) year by year over the retirement timeline until it ends or fails.
    internal static class RunSimulator
    {
        public static RunSummary Simulate(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, IWithdrawalStrategy strategy, Random random) =>
            Simulate(parameters, timeline, strategy, random, RothConversion.DefaultCeiling, out _);

        // The same run with a different Roth conversion ceiling, also handing back the final balances (the Strategy
        // Lab values what's left after tax). Conversions still happen only when EnableRothConversions is set.
        public static RunSummary Simulate(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, IWithdrawalStrategy strategy, Random random,
            ConversionCeiling conversionCeiling, out Accounts finalAccounts)
        {
            var accounts = Accounts.FromParameters(parameters);
            var years = new List<RunYearDetail>(timeline.Count);
            finalAccounts = accounts;

            foreach (var retirementYear in timeline)
            {
                double rate = DrawReturn(parameters.Mean, parameters.StdDev, random);

                var (detail, failed) = SimulateYear(parameters, strategy, conversionCeiling, accounts, retirementYear, rate);
                years.Add(detail);

                // A run fails the instant any bucket (or the total) goes negative, or the eligible buckets
                // can't cover the year's need - even while age-locked money keeps the total positive.
                if (failed)
                    return new RunSummary { Years = years, FailureYear = retirementYear.Index };
            }

            return new RunSummary { Years = years, FailureYear = null };
        }

        private static (RunYearDetail Detail, bool Failed) SimulateYear(
            SimulationParameters parameters, IWithdrawalStrategy strategy, ConversionCeiling conversionCeiling,
            Accounts accounts, RetirementYear year, double rate)
        {
            // Today's-dollar inputs, inflated to this calendar year. Spending and returns are prorated for a partial
            // year; the tax year's deduction and brackets are not (they're annual). Social Security counts the
            // monthly payments that actually fall in this year.
            var taxYear = year.TaxYear;
            double spending = parameters.Withdrawal * year.InflationFactor * year.Fraction;
            double socialSecurity = parameters.SocialSecurityMonthlyAmount * year.InflationFactor * year.SocialSecurityPayments;
            var ss = SocialSecurityYear.Compute(socialSecurity, taxYear, spending);
            double periodRate = rate * year.Fraction;

            // Start-of-year (prior year-end) Tax Deferred balance, for the "% of balance" figure
            double taxableStartOfYear = accounts.Taxable;

            // All three buckets grow by the same rate, then this year's withdrawal is taken
            accounts.ApplyReturn(periodRate);

            bool ageEligible = year.AgeEligible;
            var plan = strategy.Plan(new WithdrawalContext(
                ss.Need, ss.Taxable, taxYear,
                accounts.EligibleTaxable(ageEligible), accounts.Brokerage, accounts.BrokerageGainFraction, accounts.EligibleRoth(ageEligible)));

            accounts.WithdrawTaxable(plan.GrossTaxable);
            accounts.SellBrokerage(plan.GrossBrokerage);
            accounts.WithdrawRoth(plan.Roth);

            var conversion = parameters.EnableRothConversions
                ? RothConversion.Apply(accounts, taxYear, ss.Taxable + plan.GrossTaxable, plan.RealizedGains, conversionCeiling)
                : RothConversion.None;

            // The year's taxes come straight from its final income totals - ordinary income (taxable Social
            // Security, Tax Deferred draws, the conversion) with all realized gains stacked on top - so they
            // include the conversion and the sale funding its tax.
            double ordinaryIncome = ss.Taxable + plan.GrossTaxable + conversion.Amount;
            double realizedGains = plan.RealizedGains + conversion.RealizedGains;
            double ordinaryTax = taxYear.OrdinaryTax(ordinaryIncome);
            double capitalGainsTax = taxYear.CapitalGainsTax(ordinaryIncome, realizedGains);
            double brokerageWithdrawal = plan.GrossBrokerage + conversion.TaxSale;

            // Blended effective tax rate across all buckets (Roth always contributes 0)
            double totalGross = plan.GrossTaxable + brokerageWithdrawal + plan.Roth + ss.Gross;
            double taxRate = totalGross > 0 ? (ordinaryTax + capitalGainsTax) / totalGross : 0;

            // Leftover 0% room: realize more gains tax-free by selling and rebuying (a basis step-up, no cash)
            double harvestedGains = strategy.HarvestsZeroRateGains
                ? GainHarvest.Apply(accounts, taxYear, ordinaryIncome, realizedGains)
                : 0;

            var (bracketRate, amountUntilNextBracket, nextBracketRate) = taxYear.BracketRoom(ordinaryIncome);
            var (gainsBracketRate, amountUntilNextGainsBracket, nextGainsBracketRate) =
                taxYear.CapitalGainsBracketRoom(ordinaryIncome, realizedGains + harvestedGains);

            // New money (e.g. an inheritance) arrives as after-tax cash in its year; so does any Social
            // Security beyond this year's spending
            if (year.Index == parameters.YearNewMoney)
                accounts.DepositBrokerageCash(parameters.NewMoney);
            accounts.DepositBrokerageCash(ss.Surplus);

            // Reported return, backed out of the year-end balances
            double returnAmount = accounts.Total
                - (accounts.Taxable / (1 + periodRate) + accounts.Brokerage / (1 + periodRate) + accounts.Roth / (1 + periodRate));

            var detail = new RunYearDetail
            {
                Year = year.Index,
                CalendarYear = year.CalendarYear,
                YearFraction = year.Fraction,
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
                CapitalGainsBracketRate = gainsBracketRate,
                AmountUntilNextCapitalGainsBracket = amountUntilNextGainsBracket,
                NextCapitalGainsBracketRate = nextGainsBracketRate,
                HarvestedGains = harvestedGains,
                RealizedGains = realizedGains,
                ZeroRateGains = taxYear.ZeroRateGains(ordinaryIncome, realizedGains + harvestedGains),
                AgeEligible = ageEligible,
                RothConversionAmount = conversion.Amount,
                RothConversionTax = conversion.Tax,
                AgeInYear = year.AgeAtStart,
                TaxableWithdrawalPercentOfBalance = taxableStartOfYear > 0 ? plan.GrossTaxable / taxableStartOfYear : 0,
                SocialSecurityIncome = ss.Gross,
                SocialSecurityTax = ss.Tax,
                SocialSecurityMonths = year.SocialSecurityPayments
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
