namespace MonteCarloSimulation.Core
{
    // One simulated run: its year-by-year detail plus summary stats derived from it. A failed run's
    // Years ends with the failure year itself.
    public record RunSummary
    {
        public required IReadOnlyList<RunYearDetail> Years { get; init; }
        public required int? FailureYear { get; init; }

        public bool Failed => FailureYear.HasValue;
        public double EndingBalance => Years[^1].Balance;
        public double AverageAnnualReturn => Years.Average(y => y.RateOfReturn);
        public double AverageTaxRate => Years.Average(y => y.TaxRate);
        public double LifetimeTaxesPaid => Years.Sum(y => y.OrdinaryTaxAmount) + Years.Sum(y => y.CapitalGainsTaxAmount);
        public double LifetimeIrmaaSurcharges => Years.Sum(y => y.IrmaaSurcharge);

        // Ties resolve to the earliest year.
        public int HighestReturnYear => FirstYearOf(y => y.RateOfReturn, Years.Max(y => y.RateOfReturn));
        public double HighestReturnValue => Years.Max(y => y.RateOfReturn);
        public int LowestReturnYear => FirstYearOf(y => y.RateOfReturn, Years.Min(y => y.RateOfReturn));
        public double LowestReturnValue => Years.Min(y => y.RateOfReturn);
        public int LowestBalanceYear => FirstYearOf(y => y.Balance, Years.Min(y => y.Balance));
        public double LowestBalanceValue => Years.Min(y => y.Balance);

        private int FirstYearOf(Func<RunYearDetail, double> selector, double value) =>
            Years.First(y => selector(y) == value).Year;
    }
}
