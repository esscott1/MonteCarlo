using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MonteCarloSimulation.Optimizer;

namespace MonteCarloSimulation.Web.Tests
{
    // POST /api/run/optimize, the Scenario runner's Optimize: the Optimizer on the runner's own inputs, with 167 simulated
    // markets, every claiming age through the runner's one Social Security claim, and an annuity purchase unless one is
    // already owned - streamed like /api/optimal, and exactly what SpendingOptimizer.Optimize computes for those inputs.
    public class RunOptimizeEndpointTests(PlusAppFactory factory) : IClassFixture<PlusAppFactory>
    {
        // The Scenario runner's defaults
        private static RunRequest DefaultRequest() => new()
        {
            Years = 40,
            Iterations = 20,
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

        private async Task<HttpResponseMessage> PostAsync(string path, RunRequest request) =>
            await factory.CreateClient().PostAsync(path,
                new StringContent(JsonSerializer.Serialize(request, AppJsonOptions), Encoding.UTF8, "application/json"));

        private static List<JsonElement> Events(string body) =>
            body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();

        private static string TypeOf(JsonElement e) => e.GetProperty("type").GetString()!;

        [Fact]
        public async Task InvalidRequest_Returns400WithFieldErrors_NotAStream()
        {
            var request = DefaultRequest();
            request.Years = 0;
            request.SocialSecurityStartDate = new DateOnly(2029, 7, 7); // before 62

            using var response = await PostAsync("/api/run/optimize", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var errors = body.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty("years", out _));
            Assert.True(errors.TryGetProperty("socialSecurityStartDate", out _));
        }

        [Fact]
        public async Task TheRunnersInputs_StreamTheOptimizersResult_From167Markets_WeighingAnAnnuityPurchase()
        {
            var request = DefaultRequest();

            using var response = await PostAsync("/api/run/optimize", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(OptimalStream.ContentType, response.Content.Headers.ContentType?.MediaType);
            var events = Events(await response.Content.ReadAsStringAsync());
            Assert.Equal("start", TypeOf(events[0]));
            Assert.Equal(new[] { "result", "done" }, events.TakeLast(2).Select(TypeOf));
            Assert.Equal(167, events[0].GetProperty("paths").GetInt32());

            // What it ran: the runner's own plan, its claim's curve through every age, a purchase weighed as for Plus
            var figures = await factory.Services.GetRequiredService<SpiaPayoutIndex>().CurrentAsync();
            var purchase = OptimalRequest.PurchaseOptionsAt(figures, request.AgeAtRetirement, female: false, cola: true, afterTax: true);
            var inputs = request.ToOptimizerInputs(purchase);
            Assert.NotNull(inputs.Purchase);
            Assert.Equal(SpendingOptimizer.TotalSteps(inputs), events[0].GetProperty("totalSteps").GetInt32());
            Assert.Equal(2_750, inputs.SocialSecurity.MonthlyBenefitAtAge(62), 6); // the runner's own claim, at 62
            var expected = SpendingOptimizer.Optimize(inputs);
            Assert.Equal(JsonSerializer.Serialize(expected.Optimum, AppJsonOptions), events[^2].GetProperty("optimum").GetRawText());
        }

        [Fact]
        public async Task AScenarioThatOwnsAnAnnuity_WeighsNoPurchase()
        {
            var request = DefaultRequest();
            request.IncludeAnnuity = true;
            request.AnnuityMonthlyAmount = 2_600;
            request.AnnuityInvestment = 400_000;
            request.AnnuityType = "spia";

            using var response = await PostAsync("/api/run/optimize", request);

            var events = Events(await response.Content.ReadAsStringAsync());
            Assert.Equal(SpendingOptimizer.ClaimingAges.Count, events[0].GetProperty("totalSteps").GetInt32());
            var optimum = events[^2].GetProperty("optimum");
            Assert.Equal(JsonValueKind.Null, optimum.GetProperty("purchase").ValueKind);
            // The owned annuity is simulated as income: the result is the Optimizer's on that plan
            var expected = SpendingOptimizer.Optimize(request.ToOptimizerInputs(purchase: null));
            Assert.NotNull(request.ToOptimizerInputs(null).Template.Annuity);
            Assert.Equal(JsonSerializer.Serialize(expected.Optimum, AppJsonOptions), optimum.GetRawText());
        }

        [Fact]
        public async Task TheRunsOwnResults_AreUnchanged_ByTheSharedParameters()
        {
            // /api/run builds the same SimulationParameters it always did (RunRequest.ToParameters)
            var request = DefaultRequest();
            var parameters = request.ToParameters();

            Assert.Equal(request.Withdrawal, parameters.Withdrawal);
            Assert.Equal(request.Iterations, parameters.Iterations);
            Assert.Equal(request.SocialSecurityStartDate, parameters.SocialSecurityStartDate);
            Assert.Equal(0.6, parameters.AssetMix!.StockWeight);
            Assert.Equal(Core.WithdrawalStrategy.Automatic, parameters.WithdrawalStrategy);
            using var response = await PostAsync("/api/run", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // --- Optimize Social Security: the best birthday 62-70 at the runner's own spend ---

        private static SocialSecurityRequest SocialSecurityRequestFor(RunRequest run) => new()
        {
            Years = run.Years, Iterations = run.Iterations, Withdrawal = run.Withdrawal, Birthdate = run.Birthdate,
            RetirementDate = run.RetirementDate, InitialTaxableBalance = run.InitialTaxableBalance,
            InitialRothBasis = run.InitialRothBasis, InitialRothUnrealizedGain = run.InitialRothUnrealizedGain,
            InitialBrokerageBasis = run.InitialBrokerageBasis, InitialBrokerageUnrealizedGain = run.InitialBrokerageUnrealizedGain,
            NewMoney = run.NewMoney, YearNewMoney = run.YearNewMoney, SocialSecurityStartDate = run.SocialSecurityStartDate,
            SocialSecurityMonthlyAmount = run.SocialSecurityMonthlyAmount, AnnualStandardDeduction = run.AnnualStandardDeduction,
            EnableRothConversions = run.EnableRothConversions, StockAllocation = run.StockAllocation, BondAllocation = run.BondAllocation,
            CashAllocation = run.CashAllocation, StockReturn = run.StockReturn, StockStdDev = run.StockStdDev,
            BondReturn = run.BondReturn, BondStdDev = run.BondStdDev, CashReturn = run.CashReturn, CashStdDev = run.CashStdDev,
            StockBondCorrelation = run.StockBondCorrelation,
            SocialSecurityAt62 = 2_750, SocialSecurityAt67 = 3_900, SocialSecurityAt70 = 4_800,
        };

        private async Task<HttpResponseMessage> PostSocialSecurityAsync(SocialSecurityRequest request) =>
            await factory.CreateClient().PostAsync("/api/run/optimize-social-security",
                new StringContent(JsonSerializer.Serialize(request, AppJsonOptions), Encoding.UTF8, "application/json"));

        [Fact]
        public async Task OptimizeSocialSecurity_RefusesEstimatesThatFallWithAge()
        {
            var request = SocialSecurityRequestFor(DefaultRequest());
            request.SocialSecurityAt62 = 5_000;

            using var response = await PostSocialSecurityAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Social Security amounts should increase with age: 62 <= 67 <= 70.",
                body.RootElement.GetProperty("errors").GetProperty("socialSecurity")[0].GetString());
        }

        [Fact]
        public async Task OptimizeSocialSecurity_IsTheBestBirthday_At167Markets_AtTheRunnersOwnSpend()
        {
            var request = SocialSecurityRequestFor(DefaultRequest());

            using var response = await PostSocialSecurityAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            var expected = SocialSecurityOptimizer.BestClaim(request.ToParameters(), request.Curve, request.Withdrawal, 167);
            Assert.Equal(JsonSerializer.Serialize(expected, AppJsonOptions), body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(167, json.RootElement.GetProperty("paths").GetInt32());
            Assert.Equal(expected.Improves, json.RootElement.GetProperty("improves").GetBoolean());
        }

        [Fact]
        public async Task OptimizeSocialSecurity_KeepsAnOwnedAnnuityAsIncome()
        {
            var run = DefaultRequest();
            var request = SocialSecurityRequestFor(run);
            request.IncludeAnnuity = true;
            request.AnnuityMonthlyAmount = 2_600;
            request.AnnuityInvestment = 400_000;
            request.AnnuityType = "spia";

            using var response = await PostSocialSecurityAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(request.ToParameters().Annuity);
            var expected = SocialSecurityOptimizer.BestClaim(request.ToParameters(), request.Curve, request.Withdrawal, 167);
            Assert.Equal(JsonSerializer.Serialize(expected, AppJsonOptions), await response.Content.ReadAsStringAsync());
            // The annuity's income makes more markets survive than the same plan without it
            var without = SocialSecurityOptimizer.BestClaim(SocialSecurityRequestFor(run).ToParameters(), request.Curve, request.Withdrawal, 167);
            Assert.True(expected.Best.Survivors >= without.Best.Survivors);
        }

        [Fact]
        public async Task TheStream_IsNeverCompressed()
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/run/optimize")
            {
                Content = new StringContent(JsonSerializer.Serialize(DefaultRequest(), AppJsonOptions), Encoding.UTF8, "application/json")
            };
            message.Headers.Add("Accept-Encoding", "gzip, br");

            using var response = await factory.CreateClient().SendAsync(message, HttpCompletionOption.ResponseHeadersRead);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(response.Content.Headers.ContentEncoding);
        }
    }
}
