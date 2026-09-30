using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // Builds one year's withdrawal step by step for the lab's candidate orders: each step draws from one bucket, up
    // to a cap, until the year's net need is met. Taxes are always the year's exact totals - ordinary income
    // (taxable Social Security plus Tax Deferred draws) with every realized gain stacked on top - so the steps can
    // come in any order. Mirrors TaxOptimizedWithdrawalStrategy's accounting and clamps.
    internal sealed class YearDraw
    {
        private readonly WithdrawalContext _c;
        private readonly TaxYear _taxYear;
        private readonly double _gf;
        private readonly double _ssTax;

        public YearDraw(in WithdrawalContext context)
        {
            _c = context;
            _taxYear = context.TaxYear;
            _gf = Math.Clamp(context.GainFraction, 0.0, 1.0);
            _ssTax = _taxYear.OrdinaryTax(context.SsTaxable);
            Remaining = context.Need;
        }

        public double Remaining { get; private set; }
        public double GrossTaxable { get; private set; }
        public double GrossBrokerage { get; private set; }
        public double Roth { get; private set; }

        public double TaxableLeft => Math.Max(0, _c.EligibleTaxable) - GrossTaxable;
        public double BrokerageLeft => Math.Max(0, _c.Brokerage) - GrossBrokerage;
        public double Gains => GrossBrokerage * _gf;

        // The year's tax on top of Social Security's own, for these gross draws.
        public double ExtraTax(double grossTaxable, double grossBrokerage) =>
            _taxYear.TotalTax(_c.SsTaxable + grossTaxable, grossBrokerage * _gf) - _ssTax;

        // Cash left from these gross draws after that tax.
        public double CashAfterTax(double grossTaxable, double grossBrokerage) =>
            grossTaxable + grossBrokerage - ExtraTax(grossTaxable, grossBrokerage);

        // Tax Deferred until the year's total Tax Deferred draw reaches `grossCap` (the eligible balance at most).
        public void TaxDeferred(double grossCap = double.PositiveInfinity)
        {
            double maxGross = Math.Min(grossCap, Math.Max(0, _c.EligibleTaxable));
            if (Remaining <= 0 || maxGross <= GrossTaxable) return;

            if (Gains <= 0)
            {
                // No gains realized yet: ordinary income only, so the closed-form gross-up is exact.
                double netSoFar = GrossTaxable - _taxYear.IncrementalOrdinaryTax(_c.SsTaxable, GrossTaxable);
                double netAtMax = maxGross - _taxYear.IncrementalOrdinaryTax(_c.SsTaxable, maxGross);
                double net = Math.Min(Remaining, netAtMax - netSoFar);
                // Clamp: re-grossing can overshoot the cap by float rounding
                GrossTaxable = Math.Min(maxGross, _taxYear.GrossUpOrdinary(netSoFar + net, _c.SsTaxable));
                Remaining -= net;
                return;
            }

            // Ordinary income pushes the gains already realized into higher brackets: bisect, as the app's step 4 does.
            double sale = GrossBrokerage;
            double target = CashAfterTax(GrossTaxable, sale) + Remaining;
            double cashAtMax = CashAfterTax(maxGross, sale);
            if (cashAtMax <= target)
            {
                Remaining = target - cashAtMax;
                GrossTaxable = maxGross;
            }
            else
            {
                GrossTaxable = Bisection.Smallest(GrossTaxable, maxGross, g => CashAfterTax(g, sale) >= target);
                Remaining = 0;
            }
        }

        // Brokerage until the year's total sale reaches `grossCap` (the balance at most); its gains stack on this
        // year's ordinary income and gains so far.
        public void Brokerage(double grossCap = double.PositiveInfinity)
        {
            double left = Math.Min(grossCap, Math.Max(0, _c.Brokerage)) - GrossBrokerage;
            if (Remaining <= 0 || left <= 0) return;

            double ordinary = _c.SsTaxable + GrossTaxable;
            double netSale = Math.Min(Remaining, _taxYear.BrokerageNetCapacity(left, _gf, ordinary, Gains));
            double sale = Math.Min(left, _taxYear.GrossUpBrokerageSale(netSale, _gf, ordinary, Gains));
            GrossBrokerage += sale;
            Remaining -= netSale;
        }

        public void RothLast()
        {
            double roth = Math.Min(Math.Max(0, Remaining), Math.Max(0, _c.EligibleRoth));
            Roth += roth;
            Remaining -= roth;
        }

        public WithdrawalPlan ToPlan() =>
            WithdrawalPlan.FromGross(_c, GrossTaxable, GrossBrokerage, Roth, isShortfall: Remaining > 0.01);
    }
}
