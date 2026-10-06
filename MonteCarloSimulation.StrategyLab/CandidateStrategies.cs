using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.StrategyLab
{
    // W3: the previous "tax-optimized" order - Tax Deferred up to the start of the 22% bracket, then Brokerage,
    // then more Tax Deferred, then Roth. No gain harvesting.
    internal sealed class TaxDeferredToBracketFirstStrategy : IWithdrawalStrategy
    {
        public bool HarvestsZeroRateGains => false;

        public WithdrawalPlan Plan(in WithdrawalContext c)
        {
            var draw = new YearDraw(c);
            draw.TaxDeferred(c.TaxYear.Bracket22CeilingGross - c.SsTaxable);
            draw.Brokerage();
            draw.TaxDeferred();
            draw.RothLast();
            return draw.ToPlan();
        }
    }

    // W4: the conventional rule of thumb - Brokerage, then Tax Deferred, then Roth.
    internal sealed class BrokerageFirstStrategy : IWithdrawalStrategy
    {
        public bool HarvestsZeroRateGains => false;

        public WithdrawalPlan Plan(in WithdrawalContext c)
        {
            var draw = new YearDraw(c);
            draw.Brokerage();
            draw.TaxDeferred();
            draw.RothLast();
            return draw.ToPlan();
        }
    }

    // W5: drain Tax Deferred first - Tax Deferred, then Brokerage, then Roth.
    internal sealed class TaxDeferredFirstStrategy : IWithdrawalStrategy
    {
        public bool HarvestsZeroRateGains => false;

        public WithdrawalPlan Plan(in WithdrawalContext c)
        {
            var draw = new YearDraw(c);
            draw.TaxDeferred();
            draw.Brokerage();
            draw.RothLast();
            return draw.ToPlan();
        }
    }

    // W6: greedy and myopic - each chunk comes from whichever of Tax Deferred and Brokerage adds the least tax
    // this year per dollar drawn (ties go to Tax Deferred, whose low-bracket room is lost if unused); Roth last.
    internal sealed class GreedyMarginalStrategy : IWithdrawalStrategy
    {
        // Chunks of at least $1,000, and at most about 25 a year to keep the lab's runtime reasonable.
        private const double MinimumChunk = 1_000;
        private const int ChunksPerYear = 25;

        public bool HarvestsZeroRateGains => false;

        public WithdrawalPlan Plan(in WithdrawalContext c)
        {
            var draw = new YearDraw(c);
            double chunk = Math.Max(MinimumChunk, c.Need / ChunksPerYear);

            while (draw.Remaining > 0.01)
            {
                // A bucket with less than a cent left is exhausted (clamped draws can leave float dust behind)
                double taxableStep = draw.TaxableLeft > 0.01 ? Math.Min(chunk, draw.TaxableLeft) : 0;
                double brokerageStep = draw.BrokerageLeft > 0.01 ? Math.Min(chunk, draw.BrokerageLeft) : 0;
                if (taxableStep <= 0 && brokerageStep <= 0) break;

                double taxNow = draw.ExtraTax(draw.GrossTaxable, draw.GrossBrokerage);
                double taxableRate = taxableStep > 0
                    ? (draw.ExtraTax(draw.GrossTaxable + taxableStep, draw.GrossBrokerage) - taxNow) / taxableStep
                    : double.PositiveInfinity;
                double brokerageRate = brokerageStep > 0
                    ? (draw.ExtraTax(draw.GrossTaxable, draw.GrossBrokerage + brokerageStep) - taxNow) / brokerageStep
                    : double.PositiveInfinity;

                double before = draw.Remaining;
                if (brokerageStep <= 0 || (taxableStep > 0 && taxableRate <= brokerageRate))
                    draw.TaxDeferred(draw.GrossTaxable + taxableStep);
                else
                    draw.Brokerage(draw.GrossBrokerage + brokerageStep);
                if (draw.Remaining >= before - 1e-9) break; // no progress: nothing more to draw
            }

            draw.RothLast();
            return draw.ToPlan();
        }
    }

    // The app's own Tax-optimized order with 0% gain harvesting switched off, to isolate what harvesting adds.
    internal sealed class TaxOptimizedWithoutHarvestStrategy : IWithdrawalStrategy
    {
        private readonly TaxOptimizedWithdrawalStrategy _inner = new();

        public bool HarvestsZeroRateGains => false;

        public WithdrawalPlan Plan(in WithdrawalContext c) => _inner.Plan(c);
    }
}
