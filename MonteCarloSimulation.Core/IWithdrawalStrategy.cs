namespace MonteCarloSimulation.Core
{
    // Decides how one year's net spending need is split across the eligible buckets. Pure: returns a plan
    // and never touches the balances - the caller applies it to Accounts.
    internal interface IWithdrawalStrategy
    {
        WithdrawalPlan Plan(in WithdrawalContext context);
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

    // Net and gross (pre-tax) draw per bucket. Roth is untaxed, so net == gross there. IsShortfall means
    // the eligible buckets couldn't cover the need - the year fails.
    internal readonly record struct WithdrawalPlan(
        double NetTaxable,
        double GrossTaxable,
        double NetBrokerage,
        double GrossBrokerage,
        double Roth,
        bool IsShortfall)
    {
        public double TaxableTax => GrossTaxable - NetTaxable;
        public double CapitalGainsTax => GrossBrokerage - NetBrokerage;
    }

    internal static class WithdrawalStrategies
    {
        private static readonly IWithdrawalStrategy ProRata = new ProRataWithdrawalStrategy();
        private static readonly IWithdrawalStrategy TaxOptimized = new TaxOptimizedWithdrawalStrategy();

        public static IWithdrawalStrategy For(WithdrawalStrategy strategy) =>
            strategy == WithdrawalStrategy.TaxOptimized ? TaxOptimized : ProRata;
    }
}
