namespace MonteCarloSimulation.Core
{
    // Decides how one year's net spending need is split across the eligible buckets. Pure: returns a plan
    // and never touches the balances - the caller applies it to Accounts.
    internal interface IWithdrawalStrategy
    {
        WithdrawalPlan Plan(in WithdrawalContext context);

        // Whether leftover 0% capital gains room is harvested (sell-and-rebuy to step up Brokerage basis)
        // after the year's withdrawals and conversions.
        bool HarvestsZeroRateGains { get; }
    }

    // What a strategy sees for one year, after returns and the age gate.
    //  Need: net (after-tax) dollars the portfolio must supply.
    //  SsTaxable: taxable Social Security already stacked first in the ordinary brackets.
    //  EligibleTaxable / EligibleRoth: what the 59.5 gate currently allows. Brokerage is never gated.
    internal readonly record struct WithdrawalContext(
        double Need,
        double SsTaxable,
        TaxYear TaxYear,
        double EligibleTaxable,
        double Brokerage,
        double GainFraction,
        double EligibleRoth);

    // Net and gross (pre-tax) draw per bucket. Roth is untaxed, so net == gross there. RealizedGains is the
    // embedded gain the Brokerage sale realizes. IsShortfall means the eligible buckets couldn't cover the
    // need - the year fails. Because gains stack on ordinary income, the split of tax between the buckets is
    // an attribution: TaxableTax is the ordinary tax the Tax Deferred draw adds on top of Social Security,
    // and CapitalGainsTax is the gains tax on the sale stacked on all of the year's ordinary income.
    internal readonly record struct WithdrawalPlan(
        double NetTaxable,
        double GrossTaxable,
        double NetBrokerage,
        double GrossBrokerage,
        double RealizedGains,
        double Roth,
        bool IsShortfall)
    {
        public double TaxableTax => GrossTaxable - NetTaxable;
        public double CapitalGainsTax => GrossBrokerage - NetBrokerage;

        // Builds a plan from gross draws, attributing the year's tax between Tax Deferred and Brokerage.
        public static WithdrawalPlan FromGross(in WithdrawalContext c, double grossTaxable, double grossBrokerage, double roth, bool isShortfall)
        {
            double gains = grossBrokerage * Math.Clamp(c.GainFraction, 0.0, 1.0);
            double ordinaryIncome = c.SsTaxable + grossTaxable;
            double taxableTax = c.TaxYear.IncrementalOrdinaryTax(c.SsTaxable, grossTaxable);
            double capitalGainsTax = c.TaxYear.CapitalGainsTax(ordinaryIncome, gains);
            return new WithdrawalPlan(
                grossTaxable - taxableTax, grossTaxable, grossBrokerage - capitalGainsTax, grossBrokerage, gains, roth, isShortfall);
        }
    }

    internal static class WithdrawalStrategies
    {
        private static readonly IWithdrawalStrategy ProRata = new ProRataWithdrawalStrategy();
        private static readonly IWithdrawalStrategy TaxOptimized = new TaxOptimizedWithdrawalStrategy();

        public static IWithdrawalStrategy For(WithdrawalStrategy strategy) =>
            strategy == WithdrawalStrategy.TaxOptimized ? TaxOptimized : ProRata;
    }
}
