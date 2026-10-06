namespace MonteCarloSimulation.Core
{
    // Simulates one run (one Monte Carlo iteration) year by year over the retirement timeline until it ends or fails.
    internal static class RunSimulator
    {
        public static RunSummary Simulate(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, IWithdrawalStrategy strategy, Random random) =>
            Simulate(parameters, timeline, strategy, random, RothConversion.DefaultCeiling, out _);

        // The same run with a given Roth conversion ceiling (null: no conversions), also handing back the final
        // balances (AutomaticStrategy and the Strategy Lab value what's left after tax). Conversions still happen
        // only when EnableRothConversions is set.
        public static RunSummary Simulate(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, IWithdrawalStrategy strategy, Random random,
            ConversionCeiling? conversionCeiling, out Accounts finalAccounts)
        {
            finalAccounts = Accounts.FromParameters(parameters);
            var years = new List<RunYearDetail>(timeline.Count);
            int? failureYear = Run(parameters, timeline, strategy, random, default, conversionCeiling, finalAccounts, years);
            return new RunSummary { Years = years, FailureYear = failureYear };
        }

        // The same run, for searches that rerun a market path many times and need only its outcome: `returns` holds
        // the path's annual returns (SeededReturns), and no year-by-year report is built. Survives exactly when
        // Simulate with the Random that drew those returns doesn't fail.
        public static bool Survives(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, IWithdrawalStrategy strategy, ReadOnlySpan<double> returns,
            ConversionCeiling? conversionCeiling, out Accounts finalAccounts)
        {
            finalAccounts = Accounts.FromParameters(parameters);
            return Run(parameters, timeline, strategy, null, returns, conversionCeiling, finalAccounts, null) is null;
        }

        // The annual returns `new Random(path)` draws for each path, one per timeline year: what Simulate would draw
        // for a run seeded with the path number. Searches compute them once instead of on every run.
        public static double[][] SeededReturns(double mean, double standardDeviation, int years, int paths)
        {
            var returns = new double[paths][];
            for (int path = 0; path < paths; path++)
                returns[path] = SeededReturns(mean, standardDeviation, years, path, 1)[0];
            return returns;
        }

        // Just paths first..first+count-1.
        public static double[][] SeededReturns(double mean, double standardDeviation, int years, int first, int count)
        {
            var returns = new double[count][];
            for (int i = 0; i < count; i++)
            {
                var random = new Random(first + i);
                returns[i] = new double[years];
                for (int year = 0; year < years; year++)
                    returns[i][year] = DrawReturn(mean, standardDeviation, random);
            }
            return returns;
        }

        // The year loop shared by both modes. Each year's return comes from `random` when given, else from `returns`;
        // `years` collects the year-by-year report when given. Returns the failure year, or null if the run survives.
        private static int? Run(
            SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, IWithdrawalStrategy strategy,
            Random? random, ReadOnlySpan<double> returns, ConversionCeiling? conversionCeiling, Accounts accounts, List<RunYearDetail>? years)
        {
            // Medicare IRMAA looks back two years (MedicareIrmaa.LookbackYears); the model's first two years have no
            // lookback inside it
            double magiLastYear = 0, magiTwoYearsAgo = 0;

            foreach (var retirementYear in timeline)
            {
                double rate = random is not null ? DrawReturn(parameters.Mean, parameters.StdDev, random) : returns[retirementYear.Index];
                double? lookbackMagi = retirementYear.Index >= MedicareIrmaa.LookbackYears ? magiTwoYearsAgo : null;

                var step = SimulateYear(
                    parameters, strategy, conversionCeiling, accounts, retirementYear, BridgeNeed(parameters, timeline, retirementYear), lookbackMagi, rate);
                magiTwoYearsAgo = magiLastYear;
                magiLastYear = step.Magi;
                years?.Add(Report(retirementYear, rate, step, accounts));

                // A run fails the instant any bucket (or the total) goes negative, or the eligible buckets
                // can't cover the year's need - even while age-locked money keeps the total positive.
                if (step.Failed)
                    return retirementYear.Index;
            }

            return null;
        }

        // What one year did, for the report: everything Report needs that the balances don't show.
        private readonly record struct YearStep(
            double Spending,
            double Irmaa,
            SocialSecurityYear SocialSecurity,
            double PeriodRate,
            double TaxableStartOfYear,
            WithdrawalPlan Plan,
            RothConversion Conversion,
            double OrdinaryIncome,
            double RealizedGains,
            double HarvestedGains,
            bool Failed)
        {
            public double Magi => OrdinaryIncome + RealizedGains + HarvestedGains;
        }

        // Advances the balances through one year. Everything that changes the run's state happens here; Report only
        // describes it.
        private static YearStep SimulateYear(
            SimulationParameters parameters, IWithdrawalStrategy strategy, ConversionCeiling? conversionCeiling,
            Accounts accounts, RetirementYear year, double bridgeNeed, double? lookbackMagi, double rate)
        {
            // Today's-dollar inputs, inflated to this calendar year. Spending and returns are prorated for a partial
            // year; the tax year's deduction and brackets are not (they're annual). Social Security counts the
            // monthly payments that actually fall in this year.
            var taxYear = year.TaxYear;
            double spending = parameters.Withdrawal * year.InflationFactor * year.Fraction;
            double socialSecurity = parameters.SocialSecurityMonthlyAmount * year.InflationFactor * year.SocialSecurityPayments;
            // Medicare IRMAA surcharges are a cost of the year on top of spending
            double irmaa = MedicareIrmaa.YearSurcharge(lookbackMagi, year);
            var ss = SocialSecurityYear.Compute(socialSecurity, taxYear, spending + irmaa);
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

            var conversion = parameters.EnableRothConversions && conversionCeiling is not null
                ? RothConversion.Apply(accounts, taxYear, ss.Taxable + plan.GrossTaxable, plan.RealizedGains, conversionCeiling,
                    new ConversionFunding(parameters.ConversionTaxFunding, ageEligible, bridgeNeed))
                : RothConversion.None;

            // The year's income totals: ordinary income (taxable Social Security, Tax Deferred draws, the conversion)
            // with all realized gains stacked on top - so they include the conversion and the sale funding its tax.
            double ordinaryIncome = ss.Taxable + plan.GrossTaxable + conversion.Amount;
            double realizedGains = plan.RealizedGains + conversion.RealizedGains;

            // Leftover 0% room: realize more gains tax-free by selling and rebuying (a basis step-up, no cash)
            double harvestedGains = strategy.HarvestsZeroRateGains
                ? GainHarvest.Apply(accounts, taxYear, ordinaryIncome, realizedGains)
                : 0;

            // New money (e.g. an inheritance) arrives as after-tax cash in its year; so does any Social
            // Security beyond this year's spending
            if (year.Index == parameters.YearNewMoney)
                accounts.DepositBrokerageCash(parameters.NewMoney);
            accounts.DepositBrokerageCash(ss.Surplus);

            return new YearStep(
                spending, irmaa, ss, periodRate, taxableStartOfYear, plan, conversion, ordinaryIncome, realizedGains, harvestedGains,
                Failed: accounts.AnyNegative || plan.IsShortfall);
        }

        // The year-by-year report for a year SimulateYear just advanced: `accounts` hold its year-end balances.
        private static RunYearDetail Report(RetirementYear year, double rate, in YearStep step, Accounts accounts)
        {
            var taxYear = year.TaxYear;
            var plan = step.Plan;
            var conversion = step.Conversion;
            var ss = step.SocialSecurity;
            double ordinaryIncome = step.OrdinaryIncome;
            double realizedGains = step.RealizedGains;
            double harvestedGains = step.HarvestedGains;

            // The year's taxes come straight from its final income totals, so they include the conversion and the
            // sale funding its tax
            double ordinaryTax = taxYear.OrdinaryTax(ordinaryIncome);
            double capitalGainsTax = taxYear.CapitalGainsTax(ordinaryIncome, realizedGains);
            double brokerageWithdrawal = plan.GrossBrokerage + conversion.TaxSale;

            // Blended effective tax rate across all buckets (Roth always contributes 0)
            double totalGross = plan.GrossTaxable + brokerageWithdrawal + plan.Roth + ss.Gross;
            double taxRate = totalGross > 0 ? (ordinaryTax + capitalGainsTax) / totalGross : 0;

            var (bracketRate, amountUntilNextBracket, nextBracketRate) = taxYear.BracketRoom(ordinaryIncome);
            var (gainsBracketRate, amountUntilNextGainsBracket, nextGainsBracketRate) =
                taxYear.CapitalGainsBracketRoom(ordinaryIncome, realizedGains + harvestedGains);

            // Reported return, backed out of the year-end balances
            double periodRate = step.PeriodRate;
            double returnAmount = accounts.Total
                - (accounts.Taxable / (1 + periodRate) + accounts.Brokerage / (1 + periodRate) + accounts.Roth / (1 + periodRate));

            return new RunYearDetail
            {
                Year = year.Index,
                CalendarYear = year.CalendarYear,
                YearFraction = year.Fraction,
                RateOfReturn = rate,
                ReturnAmount = returnAmount,
                Withdrawal = step.Spending,
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
                IrmaaSurcharge = step.Irmaa,
                Magi = step.Magi,
                AgeEligible = year.AgeEligible,
                RothConversionAmount = conversion.Amount,
                RothConversionTax = conversion.Tax,
                RothConversionOrdinaryTaxFromBrokerage = conversion.OrdinaryTaxFromBrokerage,
                RothConversionOrdinaryTaxFromConversion = conversion.OrdinaryTaxFromConversion,
                TaxDeferredWithdrawalTax = plan.TaxableTax,
                AgeInYear = year.AgeAtStart,
                TaxableWithdrawalPercentOfBalance = step.TaxableStartOfYear > 0 ? plan.GrossTaxable / step.TaxableStartOfYear : 0,
                SocialSecurityIncome = ss.Gross,
                SocialSecurityTax = ss.Tax,
                SocialSecurityMonths = year.SocialSecurityPayments
            };
        }

        // Spending still to come before the 59.5 gate opens, after this year (today's-dollar spending, inflated):
        // what the BridgeAware conversion-tax rule keeps in Brokerage. 0 once the gate is open.
        private static double BridgeNeed(SimulationParameters parameters, IReadOnlyList<RetirementYear> timeline, RetirementYear year)
        {
            if (year.AgeEligible || parameters.ConversionTaxFunding != ConversionTaxFunding.BridgeAware) return 0;
            double need = 0;
            for (int i = year.Index + 1; i < timeline.Count && !timeline[i].AgeEligible; i++)
                need += parameters.Withdrawal * timeline[i].InflationFactor * timeline[i].Fraction;
            return need;
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
