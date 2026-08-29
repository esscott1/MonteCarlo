namespace MonteCarloSimulation.Core
{
    public record RunYearDetail(
        int Year,
        double RateOfReturn,
        double Withdrawal,
        double TaxableWithdrawal,
        double NontaxableWithdrawal,
        double TaxRate,
        double Balance,
        double TaxableBalance,
        double NontaxableBalance);
}
