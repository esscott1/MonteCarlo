namespace MonteCarloSimulation.Core
{
    // The three account balances for one run, plus the cost-basis scalars tracked alongside Brokerage and Roth.
    // The only code that changes balances. Basis is never grown by returns - it moves only with contributions
    // and withdrawals, shrinking proportionally on withdrawal to preserve the average-cost ratio (not per-lot).
    internal sealed class Accounts
    {
        public double Taxable { get; private set; }
        public double Brokerage { get; private set; }
        public double BrokerageBasis { get; private set; }
        public double Roth { get; private set; }
        public double RothBasis { get; private set; }

        public static Accounts FromParameters(SimulationParameters p) => new()
        {
            Taxable = p.InitialTaxableBalance,
            Brokerage = p.InitialBrokerageBasis + p.InitialBrokerageUnrealizedGain,
            BrokerageBasis = p.InitialBrokerageBasis,
            Roth = p.InitialRothBasis + p.InitialRothUnrealizedGain,
            RothBasis = p.InitialRothBasis
        };

        public double Total => Taxable + Brokerage + Roth;

        // What the balances are worth after the tax still owed on them: Tax Deferred at 78% (income tax to come),
        // Brokerage less 15% of its embedded gain, Roth in full. Used to compare what strategies leave behind, so
        // one isn't credited for leaving untaxed Tax Deferred money.
        public const double TaxDeferredAfterTax = 0.78;
        public const double GainsAfterTax = 0.85;
        public double AfterTaxValue =>
            Taxable * TaxDeferredAfterTax + Brokerage - Math.Max(0, Brokerage - BrokerageBasis) * (1 - GainsAfterTax) + Roth;

        public bool AnyNegative => Total < 0 || Taxable < 0 || Brokerage < 0 || Roth < 0;

        // Fraction of a Brokerage sale that is taxable embedded gain; the rest is a tax-free return of
        // principal. Clamped to [0, 1], so an embedded loss never produces a tax rebate.
        public double BrokerageGainFraction =>
            Brokerage > 0 ? Math.Clamp((Brokerage - BrokerageBasis) / Brokerage, 0.0, 1.0) : 0;

        // Age gate: Tax Deferred and Roth gains are locked until 59.5, mirroring real-world early-withdrawal
        // restrictions. Brokerage and Roth contributions (basis) are always accessible.
        public double EligibleTaxable(bool ageEligible) => ageEligible ? Taxable : 0;
        public double EligibleRoth(bool ageEligible) => ageEligible ? Roth : RothBasis;

        public void ApplyReturn(double rate)
        {
            Taxable *= (1 + rate);
            Brokerage *= (1 + rate);
            Roth *= (1 + rate);
        }

        public void WithdrawTaxable(double gross) => Taxable -= gross;

        public void SellBrokerage(double gross)
        {
            double before = Brokerage;
            Brokerage -= gross;
            BrokerageBasis *= before > 0 ? Brokerage / before : 0;
        }

        public void WithdrawRoth(double amount)
        {
            double before = Roth;
            Roth -= amount;
            RothBasis *= before > 0 ? Roth / before : 0;
        }

        // After-tax cash (NewMoney, surplus Social Security) - a contribution, not a gain, so it adds to
        // balance and basis alike.
        public void DepositBrokerageCash(double amount)
        {
            Brokerage += amount;
            BrokerageBasis += amount;
        }

        // Gain harvesting: gains realized by selling and immediately rebuying become basis. The balance is unchanged.
        public void StepUpBrokerageBasis(double gains) => BrokerageBasis += gains;

        // Converted dollars count as Roth basis, so they're accessible anytime under the 59.5 gate
        // (the IRS 5-year seasoning rule isn't modeled). `withheld` of the amount pays the conversion's tax and
        // never reaches Roth.
        public void ConvertToRoth(double amount, double withheld = 0)
        {
            Taxable -= amount;
            Roth += amount - withheld;
            RothBasis += amount - withheld;
        }
    }
}
