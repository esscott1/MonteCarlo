using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web
{
    // The asset mix both the Scenario runner (/api/run) and the Optimal page (/api/optimal) send, as flat request
    // fields: all fractions (0.6 = 60%) - the share in each class, each class's expected annual return and standard
    // deviation, and how closely stocks and bonds move together (-1 to 1).
    public abstract class AssetMixRequest
    {
        public double StockAllocation { get; set; }
        public double BondAllocation { get; set; }
        public double CashAllocation { get; set; }
        public double StockReturn { get; set; }
        public double StockStdDev { get; set; }
        public double BondReturn { get; set; }
        public double BondStdDev { get; set; }
        public double CashReturn { get; set; }
        public double CashStdDev { get; set; }
        public double StockBondCorrelation { get; set; }

        protected void ValidateAssetMix(Dictionary<string, string> errors)
        {
            bool allocationsInRange = true;
            foreach (var (key, value) in new[] { ("stockAllocation", StockAllocation), ("bondAllocation", BondAllocation), ("cashAllocation", CashAllocation) })
                if (!(value >= 0 && value <= 1))
                {
                    errors[key] = "Allocation must be between 0% and 100%.";
                    allocationsInRange = false;
                }
            if (allocationsInRange && Math.Abs(StockAllocation + BondAllocation + CashAllocation - 1) > 1e-6)
                errors["stockAllocation"] = "Allocations must total 100%.";
            foreach (var (key, value) in new[] { ("stockReturn", StockReturn), ("bondReturn", BondReturn), ("cashReturn", CashReturn) })
                if (!(value >= -1 && value <= 1)) errors[key] = "Return must be between -100% and 100%.";
            foreach (var (key, value) in new[] { ("stockStdDev", StockStdDev), ("bondStdDev", BondStdDev), ("cashStdDev", CashStdDev) })
                if (!(value >= 0 && value <= 1)) errors[key] = "Std. dev. must be between 0% and 100%.";
            if (!(StockBondCorrelation >= -1 && StockBondCorrelation <= 1)) errors["stockBondCorrelation"] = "Correlation must be between -1 and 1.";
        }

        public AssetMix ToAssetMix() => new()
        {
            StockWeight = StockAllocation,
            BondWeight = BondAllocation,
            CashWeight = CashAllocation,
            StockMean = StockReturn,
            StockStdDev = StockStdDev,
            BondMean = BondReturn,
            BondStdDev = BondStdDev,
            CashMean = CashReturn,
            CashStdDev = CashStdDev,
            StockBondCorrelation = StockBondCorrelation
        };

        // "60% stocks / 30% bonds / 10% cash"
        public string MixDescription() =>
            $"{Percent(StockAllocation)} stocks / {Percent(BondAllocation)} bonds / {Percent(CashAllocation)} cash";

        private static string Percent(double fraction) =>
            (fraction * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }
}
