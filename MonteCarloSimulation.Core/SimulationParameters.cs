namespace MonteCarloSimulation.Core
{
    public class SimulationParameters
    {
        // Years the money must last, counted from RetirementDate.
        public int Years { get; set; }
        public int Iterations { get; set; }
        // Annual spending, in today's (2026) dollars.
        public double Withdrawal { get; set; }
        public DateOnly Birthdate { get; set; }
        // The simulation starts here: withdrawals begin on this date, and the balances below are as of it.
        public DateOnly RetirementDate { get; set; }
        public double InitialTaxableBalance { get; set; }
        public double InitialRothBasis { get; set; }
        public double InitialRothUnrealizedGain { get; set; }
        public double InitialBrokerageBasis { get; set; }
        public double InitialBrokerageUnrealizedGain { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double NewMoney { get; set; }
        // Model year index (0 = the calendar year of RetirementDate) in which NewMoney arrives.
        public int YearNewMoney { get; set; }
        // First Social Security payment; payments then arrive monthly on the same day of the month.
        public DateOnly SocialSecurityStartDate { get; set; }
        // Monthly Social Security benefit, in today's (2026) dollars.
        public double SocialSecurityMonthlyAmount { get; set; }
        // Annual standard deduction, in today's (2026) dollars.
        public double AnnualStandardDeduction { get; set; }
        public bool EnableRothConversions { get; set; }
        public WithdrawalStrategy WithdrawalStrategy { get; set; }
        public required string ScenarioDescription { get; set; }
    }
}
