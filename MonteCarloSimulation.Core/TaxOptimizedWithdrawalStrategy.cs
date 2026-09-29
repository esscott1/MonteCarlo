namespace MonteCarloSimulation.Core
{
    // Gains-first ordered draw:
    //  1. Brokerage while its realized gains still fall in the 0% long-term capital gains band.
    //  2. Tax Deferred up to OrdinaryFillCeiling - the start of the 22% bracket, or lower if more ordinary income
    //     would push step 1's gains out of the 0% band (ordinary income stacks beneath gains).
    //  3. More Brokerage, now taxed at 15%/20%.
    //  4. More Tax Deferred into the higher brackets.
    //  5. Roth as last resort.
    // 0% gains take priority over 12%-bracket Tax Deferred income for the same room: the 0% LTCG ceiling sits
    // just below the start of the 22% bracket. The fill ceiling is a preference, not a hard cap - otherwise a
    // run fails with Tax Deferred money still on the books once Brokerage and Roth run dry. Each draw is capped
    // at what that bucket can net after tax, so no bucket goes negative; anything still unmet is a shortfall.
    internal sealed class TaxOptimizedWithdrawalStrategy : IWithdrawalStrategy
    {
        public bool HarvestsZeroRateGains => true;

        public WithdrawalPlan Plan(in WithdrawalContext c)
        {
            var taxYear = c.TaxYear;
            double gf = Math.Clamp(c.GainFraction, 0.0, 1.0);
            double brokerageAvailable = Math.Max(0, c.Brokerage);
            double remaining = c.Need;

            // 1. Brokerage up to the sale whose gains exactly fill the 0% band - untaxed, so net == gross.
            double zeroRateSaleCap = gf > 0 ? taxYear.ZeroRateGainRoom(c.SsTaxable, 0) / gf : double.PositiveInfinity;
            double grossBrokerage = Math.Min(Math.Min(remaining, brokerageAvailable), zeroRateSaleCap);
            double gains = grossBrokerage * gf;
            remaining -= grossBrokerage;

            // 2. Tax Deferred up to the fill ceiling. Income stays at or below the 0% ceiling whenever gains were
            // realized, so the ordinary-only closed-form gross-up is exact here.
            double ceiling = taxYear.OrdinaryFillCeiling(c.SsTaxable, gains);
            double maxTaxableGross = Math.Min(Math.Max(0, ceiling - c.SsTaxable), Math.Max(0, c.EligibleTaxable));
            double maxTaxableNet = maxTaxableGross - taxYear.IncrementalOrdinaryTax(c.SsTaxable, maxTaxableGross);
            double netTaxable = Math.Min(remaining, maxTaxableNet);
            // Clamp: re-grossing a net derived from the full cap can overshoot it by float rounding,
            // which would leave the bucket at -1e-10 and falsely trip the negative-balance failure.
            double grossTaxable = Math.Min(maxTaxableGross, taxYear.GrossUpOrdinary(netTaxable, c.SsTaxable));
            remaining -= netTaxable;

            // 3. More Brokerage, its gains stacked on all ordinary income so far and step 1's gains.
            if (remaining > 0)
            {
                double ordinaryIncome = c.SsTaxable + grossTaxable;
                double brokerageLeft = brokerageAvailable - grossBrokerage;
                double netSale = Math.Min(remaining, taxYear.BrokerageNetCapacity(brokerageLeft, gf, ordinaryIncome, gains));
                double sale = Math.Min(brokerageLeft, taxYear.GrossUpBrokerageSale(netSale, gf, ordinaryIncome, gains));
                grossBrokerage += sale;
                gains += sale * gf;
                remaining -= netSale;
            }

            // 4. More Tax Deferred. Extra ordinary income also pushes the gains already realized into higher
            // LTCG brackets, so there's no closed form: bisect for the smallest Tax Deferred draw whose after-tax
            // cash (net of both taxes) covers what's still needed. That cash rises with every gross dollar
            // (at least 1 - 37% - 20%), so the search is monotone.
            if (remaining > 0 && c.EligibleTaxable > grossTaxable)
            {
                double ssTaxable = c.SsTaxable;
                double brokerageGains = gains;
                double saleTotal = grossBrokerage;
                double ssTax = taxYear.OrdinaryTax(ssTaxable);
                double CashAfterTax(double taxableGross) =>
                    taxableGross + saleTotal - (taxYear.TotalTax(ssTaxable + taxableGross, brokerageGains) - ssTax);

                double target = CashAfterTax(grossTaxable) + remaining;
                double maxGross = c.EligibleTaxable;
                double cashAtMax = CashAfterTax(maxGross);
                if (cashAtMax <= target)
                {
                    remaining = target - cashAtMax;
                    grossTaxable = maxGross;
                }
                else
                {
                    grossTaxable = Bisection.Smallest(grossTaxable, maxGross, g => CashAfterTax(g) >= target);
                    remaining = 0;
                }
            }

            // 5. Roth
            double roth = Math.Min(remaining, Math.Max(0, c.EligibleRoth));
            remaining -= roth;

            return WithdrawalPlan.FromGross(c, grossTaxable, grossBrokerage, roth, isShortfall: remaining > 0.01);
        }
    }
}
