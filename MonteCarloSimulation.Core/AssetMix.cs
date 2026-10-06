namespace MonteCarloSimulation.Core
{
    // How the portfolio is invested: the share in stocks, bonds and cash, and each class's expected annual return and
    // standard deviation (all fractions: 0.08 = 8%). Every account (Tax Deferred, Roth, Brokerage) holds the same mix,
    // rebalanced to it each year, so every account earns the same blended return; rebalancing inside Brokerage is
    // assumed to realize no gains. Stocks and bonds move together by StockBondCorrelation; cash moves on its own.
    public sealed record AssetMix
    {
        public double StockWeight { get; init; }
        public double BondWeight { get; init; }
        public double CashWeight { get; init; }
        public double StockMean { get; init; }
        public double StockStdDev { get; init; }
        public double BondMean { get; init; }
        public double BondStdDev { get; init; }
        public double CashMean { get; init; }
        public double CashStdDev { get; init; }
        public double StockBondCorrelation { get; init; }

        // The blended annual return's mean and standard deviation.
        public double ExpectedReturn => StockWeight * StockMean + BondWeight * BondMean + CashWeight * CashMean;

        public double StdDev
        {
            get
            {
                double stocks = StockWeight * StockStdDev, bonds = BondWeight * BondStdDev, cash = CashWeight * CashStdDev;
                return Math.Sqrt(stocks * stocks + bonds * bonds + cash * cash + 2 * StockBondCorrelation * stocks * bonds);
            }
        }

        // One year's blended return: three standard normals, the bond one correlated with the stock one.
        internal double DrawReturn(Random random)
        {
            double stockZ = RunSimulator.StandardNormal(random);
            double bondZ = StockBondCorrelation * stockZ
                + Math.Sqrt(1 - StockBondCorrelation * StockBondCorrelation) * RunSimulator.StandardNormal(random);
            double cashZ = RunSimulator.StandardNormal(random);
            return StockWeight * (StockMean + StockStdDev * stockZ)
                + BondWeight * (BondMean + BondStdDev * bondZ)
                + CashWeight * (CashMean + CashStdDev * cashZ);
        }
    }
}
