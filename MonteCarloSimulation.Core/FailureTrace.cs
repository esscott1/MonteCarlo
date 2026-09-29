using System.Text;

namespace MonteCarloSimulation.Core
{
    // Plain-text year-by-year trace of every failed run, in run order, which both front ends display when
    // any run fails.
    internal static class FailureTrace
    {
        public static string Build(IEnumerable<RunSummary> runs)
        {
            var trace = new StringBuilder();
            foreach (var run in runs.Where(r => r.Failed))
            {
                foreach (var y in run.Years)
                {
                    trace.Append($"\nYear {y.Year}\nRate of return: {y.RateOfReturn:P2} \nwithdrawal: {y.Withdrawal:C0}(taxable {y.TaxableWithdrawal:C0}, brokerage {y.BrokerageWithdrawal:C0}, roth {y.RothWithdrawal:C0}) \ntax rate: {y.TaxRate:P2} \nbal: {y.Balance:C0} (tax {y.TaxableBalance:C0}, brokerage {y.BrokerageBalance:C0}, roth {y.RothBalance:C0})\n");
                }
                trace.Append('\n');
            }
            return trace.ToString();
        }
    }
}
