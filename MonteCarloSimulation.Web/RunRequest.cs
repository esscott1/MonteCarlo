using MonteCarloSimulation.Core;

namespace MonteCarloSimulation.Web
{
    public class RunRequest
    {
        public int Years { get; set; }
        public int Iterations { get; set; }
        public double Withdrawal { get; set; }
        public DateOnly Birthdate { get; set; }
        public DateOnly RetirementDate { get; set; }
        public double InitialTaxableBalance { get; set; }
        public double InitialRothBasis { get; set; }
        public double InitialRothUnrealizedGain { get; set; }
        public double InitialBrokerageBasis { get; set; }
        public double InitialBrokerageUnrealizedGain { get; set; }
        public double NewMoney { get; set; }
        public int YearNewMoney { get; set; }
        public DateOnly SocialSecurityStartDate { get; set; }
        public double SocialSecurityMonthlyAmount { get; set; }
        public double AnnualStandardDeduction { get; set; }
        public bool EnableRothConversions { get; set; }

        // The asset mix, all fractions (0.6 = 60%): the share in each class, each class's expected annual return and
        // standard deviation, and how closely stocks and bonds move together (-1 to 1).
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

        public Dictionary<string, string> Validate()
        {
            var errors = new Dictionary<string, string>();
            if (Years <= 0) errors["years"] = "Years must be a positive integer.";
            if (Iterations <= 0) errors["iterations"] = "Iterations must be a positive integer.";
            if (Withdrawal < 0) errors["withdrawal"] = "Withdrawal must be non-negative.";
            if (Birthdate == default || Birthdate > DateOnly.FromDateTime(DateTime.Today) || Birthdate < DateOnly.FromDateTime(DateTime.Today).AddYears(-120))
                errors["birthdate"] = "Birthdate must be a valid date in the past.";
            else
            {
                if (RetirementDate < new DateOnly(FederalTaxBrackets.Year, 1, 1) || RetirementDate <= Birthdate || RetirementDate > Birthdate.AddYears(100))
                    errors["retirementDate"] = $"Retire On Date must be after your birthdate, no earlier than {FederalTaxBrackets.Year}, and before age 100.";
                if (SocialSecurityStartDate < Birthdate.AddYears(62) || SocialSecurityStartDate > Birthdate.AddYears(70))
                    errors["socialSecurityStartDate"] = "Social Security can start between ages 62 and 70.";
            }
            if (InitialTaxableBalance < 0) errors["initialTaxableBalance"] = "Initial taxable balance must be non-negative.";
            if (InitialRothBasis < 0) errors["initialRothBasis"] = "Initial Roth basis must be non-negative.";
            if (InitialRothUnrealizedGain < 0) errors["initialRothUnrealizedGain"] = "Initial Roth unrealized gain must be non-negative.";
            if (InitialBrokerageBasis < 0) errors["initialBrokerageBasis"] = "Initial Brokerage basis must be non-negative.";
            if (InitialBrokerageUnrealizedGain < 0) errors["initialBrokerageUnrealizedGain"] = "Initial Brokerage unrealized gain must be non-negative.";
            if (NewMoney < 0) errors["newMoney"] = "New money must be non-negative.";
            if (YearNewMoney < 0) errors["yearNewMoney"] = "Year of new money must be non-negative.";
            if (SocialSecurityMonthlyAmount < 0) errors["socialSecurityMonthlyAmount"] = "Must be non-negative.";
            if (AnnualStandardDeduction < 0) errors["annualStandardDeduction"] = "Must be non-negative.";
            ValidateAssetMix(errors);
            return errors;
        }

        private void ValidateAssetMix(Dictionary<string, string> errors)
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
