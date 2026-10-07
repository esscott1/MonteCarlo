using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MonteCarloSimulation.Web.Tests
{
    // POST /api/run's asset mix: the Scenario runner sends a stocks/bonds/cash allocation (fractions that must total 1),
    // each class's return and std. dev., and the stock-bond correlation. The server checks them and echoes the mix back.
    public class RunEndpointTests(PlusAppFactory factory) : IClassFixture<PlusAppFactory>
    {
        // The Scenario runner's default inputs (withdrawal annual, mix as fractions)
        private static RunRequest DefaultRequest() => new()
        {
            Years = 40,
            Iterations = 5,
            Withdrawal = 96_000,
            Birthdate = new DateOnly(1969, 7, 7),
            RetirementDate = new DateOnly(2027, 1, 1),
            InitialTaxableBalance = 1_000_000,
            InitialRothBasis = 15_000,
            InitialRothUnrealizedGain = 5_000,
            InitialBrokerageBasis = 100_000,
            InitialBrokerageUnrealizedGain = 300_000,
            NewMoney = 1_000_000,
            YearNewMoney = 10,
            SocialSecurityStartDate = new DateOnly(2031, 7, 7),
            SocialSecurityMonthlyAmount = 2_750,
            AnnualStandardDeduction = 16_000,
            EnableRothConversions = true,
            StockAllocation = 0.6,
            BondAllocation = 0.3,
            CashAllocation = 0.1,
            StockReturn = 0.08,
            StockStdDev = 0.19,
            BondReturn = 0.045,
            BondStdDev = 0.04,
            CashReturn = 0.035,
            CashStdDev = 0.01,
            StockBondCorrelation = 0.1
        };

        private JsonSerializerOptions AppJsonOptions => factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        private async Task<HttpResponseMessage> PostAsync(RunRequest request) =>
            await factory.CreateClient().PostAsync("/api/run",
                new StringContent(JsonSerializer.Serialize(request, AppJsonOptions), Encoding.UTF8, "application/json"));

        private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("errors").Clone();
        }

        [Fact]
        public async Task TheDefaults_Run_AndEchoTheMix()
        {
            using var response = await PostAsync(DefaultRequest());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var parameters = body.RootElement.GetProperty("parameters");
            var mix = parameters.GetProperty("assetMix");
            Assert.Equal(0.6, mix.GetProperty("stockWeight").GetDouble());
            Assert.Equal(0.1, mix.GetProperty("stockBondCorrelation").GetDouble());
            Assert.Equal(0.065, parameters.GetProperty("mean").GetDouble(), 12);
            Assert.Equal(Math.Sqrt(0.0134146), parameters.GetProperty("stdDev").GetDouble(), 12);
            Assert.Equal("60% stocks / 30% bonds / 10% cash", parameters.GetProperty("scenarioDescription").GetString());
            Assert.Equal(5, body.RootElement.GetProperty("output").GetProperty("result").GetProperty("runs").GetArrayLength());
        }

        // Omitted (an older page) means single; "married" files jointly. The engine's choice is echoed by name.
        [Theory]
        [InlineData(null, "Single")]
        [InlineData("single", "Single")]
        [InlineData("married", "MarriedJoint")]
        [InlineData("Married", "MarriedJoint")]
        public async Task TheFilingStatus_IsEchoed(string? filingStatus, string expected)
        {
            var request = DefaultRequest();
            request.FilingStatus = filingStatus;

            using var response = await PostAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expected, body.RootElement.GetProperty("parameters").GetProperty("filingStatus").GetString());
        }

        [Fact]
        public async Task AnUnknownFilingStatus_IsA400FieldError()
        {
            var request = DefaultRequest();
            request.FilingStatus = "widowed";

            var errors = await ErrorsAsync(await PostAsync(request));

            Assert.Equal(FilingStatusInput.Message, errors.GetProperty("filingStatus")[0].GetString());
        }

        [Fact]
        public async Task TheChartBands_StartAtTheStartingBalance_AndStayInOrder()
        {
            using var response = await PostAsync(DefaultRequest());

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var bands = body.RootElement.GetProperty("output").GetProperty("balanceBands").EnumerateArray().ToList();
            // The retirement date, then the end of each of the 40 years (Jan 1 retirement: whole calendar years)
            Assert.Equal(41, bands.Count);
            Assert.Equal("2027-01-01", bands[0].GetProperty("date").GetString());
            Assert.Equal("2067-01-01", bands[^1].GetProperty("date").GetString());
            Assert.All(new[] { "lower", "middle", "upper" }, key => Assert.Equal(1_420_000, bands[0].GetProperty(key).GetDouble()));
            Assert.All(bands, b => Assert.True(
                b.GetProperty("lower").GetDouble() <= b.GetProperty("middle").GetDouble()
                && b.GetProperty("middle").GetDouble() <= b.GetProperty("upper").GetDouble()));
        }

        [Fact]
        public async Task AnAllocationThatDoesNotTotal100Percent_IsA400()
        {
            var request = DefaultRequest();
            request.CashAllocation = 0.2;

            var errors = await ErrorsAsync(await PostAsync(request));

            Assert.Equal("Allocations must total 100%.", errors.GetProperty("stockAllocation")[0].GetString());
        }

        [Fact]
        public async Task OutOfRangeMixValues_AreEach400FieldErrors()
        {
            var request = DefaultRequest();
            request.BondAllocation = -0.1;
            request.CashAllocation = 0.5; // in range, so only bondAllocation is named, not the total
            request.StockReturn = 1.5;
            request.CashStdDev = -0.01;
            request.StockBondCorrelation = 1.2;

            var errors = await ErrorsAsync(await PostAsync(request));

            Assert.Equal("Allocation must be between 0% and 100%.", errors.GetProperty("bondAllocation")[0].GetString());
            Assert.False(errors.TryGetProperty("stockAllocation", out _));
            Assert.True(errors.TryGetProperty("stockReturn", out _));
            Assert.True(errors.TryGetProperty("cashStdDev", out _));
            Assert.True(errors.TryGetProperty("stockBondCorrelation", out _));
        }
    }
}
