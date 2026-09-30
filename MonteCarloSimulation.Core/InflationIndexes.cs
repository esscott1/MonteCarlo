namespace MonteCarloSimulation.Core
{
    // The inflation-indexed amounts for one run, advanced once per simulated year: spending, the standard
    // deduction, the bracket scaling factor, and the Social Security benefit. Inflation is 2.5% for the first
    // 20 years and 1% after. Everything - year 0 included - is inflated before that year uses it.
    internal sealed class InflationIndexes
    {
        private readonly SimulationParameters _parameters;
        private double _inflation = 0.025;

        public InflationIndexes(SimulationParameters parameters)
        {
            _parameters = parameters;
            Withdrawal = parameters.Withdrawal;
            StandardDeduction = parameters.AnnualStandardDeduction;
        }

        public double Withdrawal { get; private set; }
        public double StandardDeduction { get; private set; }
        public double BracketFactor { get; private set; } = 1.0;

        // Starts at the entered amount (not inflated for the years before it begins), then compounds.
        public double SocialSecurity { get; private set; }

        public TaxYear TaxYear => new(StandardDeduction, BracketFactor, FederalTaxBrackets.Single2026, FederalTaxBrackets.CapitalGainsSingle2026);

        public void Advance(int year)
        {
            if (year > 19) _inflation = 0.01;

            if (year == _parameters.SocialSecurityYearsUntilStart)
                SocialSecurity = _parameters.SocialSecurityAnnualAmount;
            else if (year > _parameters.SocialSecurityYearsUntilStart)
                SocialSecurity *= (1 + _inflation);

            Withdrawal *= (1 + _inflation);
            StandardDeduction *= (1 + _inflation);
            BracketFactor *= (1 + _inflation);
        }
    }
}
