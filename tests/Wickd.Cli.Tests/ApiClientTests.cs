using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Services;
using Xunit;

namespace Wickd.Cli.Tests;

public sealed class ApiClientTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return ResponseToReturn;
        }
    }

    [Fact]
    public async Task FetchUsesTheCanonicalApiContract()
    {
        var handler = Handler(
            """
            {"schemaVersion":1,"contract":"fetch","provenance":{"generatedAtUtc":"2026-08-27T12:00:00Z","applicationVersion":"0.1.0","inspectionRunId":null,"sourceInspectionSchemaVersion":null},"marketId":"BTC_USDT_PERP","exchangeId":"binance","exchangeSymbol":"BTC/USDT:USDT","timeframe":"4h","fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-08-01T00:00:00Z","candleCount":1500,"cacheHit":false}
            """);
        var client = Client(handler, token: "secret-token-xyz");

        var result = await client.FetchCandlesAsync(new FetchHistoricalCandlesRequest
        {
            MarketId = "BTC_USDT_PERP",
            ExchangeId = "binance",
            ExchangeSymbol = "BTC/USDT:USDT",
            Timeframe = "4h",
            FromUtc = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            ToUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z")
        });

        result.CandleCount.Should().Be(1500);
        result.ExchangeSymbol.Should().Be("BTC/USDT:USDT");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/fetch");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("secret-token-xyz");
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("exchangeSymbol").GetString().Should().Be("BTC/USDT:USDT");
        body.RootElement.TryGetProperty("alias", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("force", out _).Should().BeFalse();
    }

    [Fact]
    public async Task BacktestSendsAnAliasSelectorWithoutCandles()
    {
        var handler = Handler(
            """
            {"schemaVersion":1,"contract":"backtest","provenance":{"generatedAtUtc":"2026-08-27T12:00:00Z","applicationVersion":"0.1.0","inspectionRunId":null,"sourceInspectionSchemaVersion":null},"runId":"test-run-001","marketId":"BTC_USDT_PERP","exchangeId":"binance","exchangeSymbol":"BTC/USDT:USDT","timeframe":"4h","fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-08-01T00:00:00Z","candleCount":500,"eventCount":12,"gapCount":0}
            """);
        var client = Client(handler);

        var result = await client.RunBacktestAsync(new BacktestRequest
        {
            RunId = "test-run-001",
            Dataset = new CachedDatasetSelector { Alias = "jul-btc" }
        });

        result.EventCount.Should().Be(12);
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("dataset").GetProperty("alias").GetString().Should().Be("jul-btc");
        body.RootElement.GetProperty("candles").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("pivotStrength").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task VwapReadsDecimalStringsFromTheCanonicalResult()
    {
        var handler = Handler(
            """
            {"schemaVersion":1,"contract":"vwap-analysis","provenance":{"generatedAtUtc":"2026-08-27T12:00:00Z","applicationVersion":"0.1.0","inspectionRunId":null,"sourceInspectionSchemaVersion":null},"marketId":"BTC_USDT_PERP","exchangeId":"binance","timeframe":"4h","fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-07-01T04:00:00Z","candleCount":1,"gapCount":0,"series":[{"period":"daily","points":[{"openTimeUtc":"2026-07-01T00:00:00Z","runningVwap":"101.25","previousClose":null}]}],"levels":[{"period":"daily","price":"100.5","bornAtUtc":"2026-07-01T00:00:00Z","expiresAtUtc":"2026-07-02T00:00:00Z","sweptAtUtc":null}],"classifications":[{"openTimeUtc":"2026-07-01T00:00:00Z","volumeClass":"large","isUp":true,"score":"2.5"}]}
            """);
        var client = Client(handler);

        var result = await client.AnalyzeVwapAsync(new VwapAnalysisRequest
        {
            Dataset = new CachedDatasetSelector { Alias = "jul-btc" }
        });

        result.Series.Single().Points.Single().RunningVwap.Should().Be(101.25m);
        result.Levels.Single().Price.Should().Be(100.5m);
        result.Classifications.Single().Score.Should().Be(2.5m);
        result.CandleCount.Should().Be(1);
    }

    [Fact]
    public async Task AccountsUnwrapsTheCanonicalEnvelope()
    {
        var handler = Handler(
            """
            {"schemaVersion":1,"contract":"accounts","provenance":{"generatedAtUtc":"2026-08-27T12:00:00Z","applicationVersion":"0.1.0","inspectionRunId":null,"sourceInspectionSchemaVersion":null},"accounts":[{"id":"acc-1","name":"Backtest USDT","kind":"backtest","currency":"USDT","capitalAllocations":[],"riskProfiles":[],"walletObservations":[],"equityObservations":[{"id":"eq-1","accountId":"acc-1","equity":"50000","observedAtUtc":"2026-08-27T00:00:00Z","knownAtUtc":"2026-08-27T00:00:00Z","sequence":0}],"cashMovements":[]}]}
            """);
        var client = Client(handler);

        var payload = await client.GetAccountsAsync();

        payload.SchemaVersion.Should().Be(1);
        payload.Contract.Should().Be("accounts");
        payload.Data.Should().ContainKey("provenance");
        payload.Accounts.Should().ContainSingle();
        payload.Accounts[0].Id.Should().Be("acc-1");
        payload.Accounts[0].EquityObservations.Single().Equity.Should().Be(50000m);
        payload.Accounts[0].Data.Should().ContainKeys(
            "capitalAllocations", "riskProfiles", "walletObservations", "cashMovements");
    }

    [Fact]
    public async Task AContractFromTheFutureIsRefused()
    {
        var handler = Handler(
            """
            {"schemaVersion":2,"contract":"fetch","marketId":"BTC_USDT_PERP","exchangeId":"binance","exchangeSymbol":"BTC/USDT:USDT","timeframe":"4h","fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-08-01T00:00:00Z","candleCount":1,"cacheHit":true}
            """);
        var client = Client(handler);

        var action = () => client.FetchCandlesAsync(new FetchHistoricalCandlesRequest());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected 'fetch' v1*");
    }

    private static MockHttpMessageHandler Handler(string json) => new()
    {
        ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }
    };

    private static WickdApiClient Client(MockHttpMessageHandler handler, string? token = null) =>
        new(
            new HttpClient(handler),
            new WickdCliConfig
            {
                ApiUrl = "https://api.wickdalgo.test",
                ApiToken = token
            });
}
