using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Services;
using Xunit;

namespace Wickd.Cli.Tests;

public class ApiClientTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return ResponseToReturn;
        }
    }

    [Fact]
    public async Task FetchCandlesAsync_SendsCorrectRequest_AndParsesResponse()
    {
        var mockHandler = new MockHttpMessageHandler();
        var expectedResponse = new FetchResultDto
        {
            MarketId = "BTC_USDT_PERP",
            Timeframe = "4h",
            CandlesFetched = 1500,
            Gaps = 0,
            Alias = "jul-btc"
        };

        mockHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(expectedResponse))
        };

        var httpClient = new HttpClient(mockHandler);
        var config = new WickdCliConfig
        {
            ApiUrl = "https://api.wickdalgo.test",
            ApiToken = "secret-token-xyz"
        };

        var client = new WickdApiClient(httpClient, config);

        var request = new FetchHistoricalCandlesRequest
        {
            MarketId = "BTC_USDT_PERP",
            Timeframe = "4h",
            ExchangeId = "binance",
            FromUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            ToUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            Alias = "jul-btc"
        };

        var result = await client.FetchCandlesAsync(request);

        result.Should().NotBeNull();
        result.MarketId.Should().Be("BTC_USDT_PERP");
        result.CandlesFetched.Should().Be(1500);
        result.Alias.Should().Be("jul-btc");

        mockHandler.LastRequest.Should().NotBeNull();
        mockHandler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        mockHandler.LastRequest.RequestUri!.ToString().Should().Be("https://api.wickdalgo.test/api/fetch");
        mockHandler.LastRequest.Headers.Authorization.Should().NotBeNull();
        mockHandler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        mockHandler.LastRequest.Headers.Authorization!.Parameter.Should().Be("secret-token-xyz");
    }

    [Fact]
    public async Task RunBacktestAsync_SendsCorrectRequest_AndParsesResponse()
    {
        var mockHandler = new MockHttpMessageHandler();
        var expectedResponse = new BacktestResultDto
        {
            RunId = "test-run-001",
            MarketId = "BTC_USDT_PERP",
            Timeframe = "4h",
            CandlesCount = 500,
            StructureEventsCount = 12
        };

        mockHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(expectedResponse))
        };

        var httpClient = new HttpClient(mockHandler);
        var config = new WickdCliConfig { ApiUrl = "https://api.wickdalgo.test" };
        var client = new WickdApiClient(httpClient, config);

        var request = new BacktestRequest
        {
            DatasetAlias = "jul-btc",
            RunId = "test-run-001"
        };

        var result = await client.RunBacktestAsync(request);

        result.Should().NotBeNull();
        result.RunId.Should().Be("test-run-001");
        result.StructureEventsCount.Should().Be(12);
        mockHandler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.wickdalgo.test/api/backtest");
    }

    [Fact]
    public async Task AnalyzeVwapAsync_SendsCorrectRequest()
    {
        var mockHandler = new MockHttpMessageHandler();
        var expectedResponse = new VwapAnalysisResultDto
        {
            MarketId = "BTC_USDT_PERP",
            Timeframe = "4h",
            CandlesAnalyzed = 194,
            VolumeSummary = new VolumeClassificationSummaryDto { Large = 6, Medium = 9, Low = 23, None = 156 }
        };

        mockHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(expectedResponse))
        };

        var httpClient = new HttpClient(mockHandler);
        var config = new WickdCliConfig { ApiUrl = "https://api.wickdalgo.test" };
        var client = new WickdApiClient(httpClient, config);

        var request = new VwapAnalysisRequest
        {
            DatasetAlias = "jul-btc",
            Periods = ["daily", "weekly"]
        };

        var result = await client.AnalyzeVwapAsync(request);

        result.Should().NotBeNull();
        result.CandlesAnalyzed.Should().Be(194);
        result.VolumeSummary.Large.Should().Be(6);
        mockHandler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.wickdalgo.test/api/analyze/vwap");
    }

    [Fact]
    public async Task GetAccountsAsync_SendsGet_AndParsesList()
    {
        var mockHandler = new MockHttpMessageHandler();
        var expectedAccounts = new List<AccountDto>
        {
            new() { AccountId = "acc-1", Name = "Binance Main", Balance = 50000m, Currency = "USDT", IsActive = true }
        };

        mockHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(expectedAccounts))
        };

        var httpClient = new HttpClient(mockHandler);
        var config = new WickdCliConfig { ApiUrl = "https://api.wickdalgo.test" };
        var client = new WickdApiClient(httpClient, config);

        var accounts = await client.GetAccountsAsync();

        accounts.Should().HaveCount(1);
        accounts[0].AccountId.Should().Be("acc-1");
        mockHandler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.wickdalgo.test/api/accounts");
    }
}
