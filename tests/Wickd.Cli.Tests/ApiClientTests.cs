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
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }
        public List<string> RequestPaths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return ResponseFactory?.Invoke(request) ?? ResponseToReturn;
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
    public async Task RunResolvesFriendlyAliasToCanonicalCachedDatasetIdentity()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath switch
                {
                    "/api/dataset-aliases" => """[{"schemaVersion":1,"contract":"dataset-alias","alias":"jul-btc","marketId":"BTC_USDT_PERP","exchangeId":"binance","timeframe":"4h","fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-08-01T00:00:00Z"}]""",
                    "/api/datasets" => """[{"schemaVersion":1,"contract":"dataset-listing","datasetAlias":"cache-key","marketId":"BTC_USDT_PERP","exchangeId":"binance","exchangeSymbol":"BTC/USDT:USDT","timeframe":"4h","fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-08-01T00:00:00Z"}]""",
                    "/api/runs" => """{"schemaVersion":1,"contract":"run","provenance":{"generatedAtUtc":"2026-08-27T12:00:00Z","applicationVersion":"0.1.0","inspectionRunId":null,"sourceInspectionSchemaVersion":null},"runId":"test-run-001","marketId":"BTC_USDT_PERP","exchangeId":"binance","exchangeSymbol":"BTC/USDT:USDT","timeframe":"4h","fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-08-01T00:00:00Z","candleCount":500,"eventCount":12,"gapCount":0}""",
                    _ => throw new InvalidOperationException("Unexpected API path.")
                }, Encoding.UTF8, "application/json")
            }
        };
        var client = Client(handler);

        var result = await client.RunBacktestAsync(new BacktestRequest
        {
            RunId = "test-run-001",
            Dataset = new CachedDatasetSelector { Alias = "jul-btc" }
        });

        result.EventCount.Should().Be(12);
        handler.RequestPaths.Should().Equal("/api/dataset-aliases", "/api/datasets", "/api/runs");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/runs");
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var dataset = body.RootElement.GetProperty("dataset");
        dataset.GetProperty("alias").ValueKind.Should().Be(JsonValueKind.Null);
        dataset.GetProperty("marketId").GetString().Should().Be("BTC_USDT_PERP");
        dataset.GetProperty("exchangeId").GetString().Should().Be("binance");
        dataset.GetProperty("exchangeSymbol").GetString().Should().Be("BTC/USDT:USDT");
        dataset.GetProperty("timeframe").GetString().Should().Be("4h");
        dataset.GetProperty("fromUtc").GetDateTimeOffset().Should().Be(DateTimeOffset.Parse("2026-07-01T00:00:00Z"));
        dataset.GetProperty("toUtc").GetDateTimeOffset().Should().Be(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        body.RootElement.TryGetProperty("candles", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("marketId", out _).Should().BeFalse();
        body.RootElement.GetProperty("pivotStrength").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task WorkspaceListForwardsIndependentHostAndUserCredentials()
    {
        var handler = Handler("""{"schemaVersion":2,"contract":"workspace-list","requestId":"req-1","asOf":"2026-09-01T00:00:00Z","workspaces":[]}""");
        var client = Client(handler, "host-token", "user-session");

        var result = await client.GetWorkspacesAsync();

        result.Workspaces.Should().BeEmpty();
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("host-token");
        handler.LastRequest.Headers.GetValues("X-Wickd-Session").Single().Should().Be("user-session");
    }

    [Fact]
    public async Task StrategyListForwardsSessionAndSelectedWorkspace()
    {
        var handler = Handler("""{"contract":"strategy-list-page","schemaVersion":2,"requestId":"req-2","asOf":"2026-09-01T00:00:00Z","data":{"items":[],"nextCursor":null,"publicationWatermark":4}}""");
        var client = Client(handler, "host-token", "user-session", "workspace-7");

        var result = await client.GetStrategiesAsync();

        result.Data.Items.Should().BeEmpty();
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("host-token");
        handler.LastRequest.Headers.GetValues("X-Wickd-Session").Single().Should().Be("user-session");
        handler.LastRequest.Headers.GetValues("X-Wickd-Workspace").Single().Should().Be("workspace-7");
    }

    [Fact]
    public async Task StrategyListReadsEveryStableCursorPage()
    {
        var handler = new MockHttpMessageHandler
        {
            ResponseFactory = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.Query.Contains("cursor=", StringComparison.Ordinal)
                    ? """{"contract":"strategy-list-page","schemaVersion":2,"requestId":"req-2","asOf":"2026-09-01T00:00:00Z","data":{"items":[{"strategyId":"strategy-2","name":"Second","description":"d","researchStatus":"testing","metadataRevision":1,"archived":false,"createdAtUtc":"2026-09-01T00:00:00Z","publishedVersionCount":0}],"nextCursor":null,"publicationWatermark":4}}"""
                    : """{"contract":"strategy-list-page","schemaVersion":2,"requestId":"req-1","asOf":"2026-09-01T00:00:00Z","data":{"items":[{"strategyId":"strategy-1","name":"First","description":"d","researchStatus":"testing","metadataRevision":1,"archived":false,"createdAtUtc":"2026-09-01T00:00:00Z","publishedVersionCount":1}],"nextCursor":"signed+/cursor==","publicationWatermark":4}}""",
                    Encoding.UTF8, "application/json")
            }
        };
        var client = Client(handler, "host-token", "user-session", "workspace-7");

        var result = await client.GetStrategiesAsync();

        result.Data.Items.Select(item => item.StrategyId).Should().Equal("strategy-1", "strategy-2");
        handler.RequestPaths.Should().HaveCount(2);
        handler.LastRequest!.RequestUri!.Query.Should().Contain("cursor=signed%2B%2Fcursor%3D%3D");
    }

    [Fact]
    public async Task WorkspaceDetailReadsMembershipScopedRoute()
    {
        var handler = Handler("""{"schemaVersion":2,"contract":"workspace-detail","requestId":"req-3","asOf":"2026-09-01T00:00:00Z","workspace":{"workspaceId":"workspace-7","name":"Research","role":"editor","personal":false,"createdAtUtc":"2026-09-01T00:00:00Z"}}""");
        var client = Client(handler, "host-token", "user-session");

        var result = await client.GetWorkspaceAsync("workspace-7");

        result.Workspace.WorkspaceId.Should().Be("workspace-7");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/v2/workspaces/workspace-7");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("host-token");
        handler.LastRequest.Headers.GetValues("X-Wickd-Session").Single().Should().Be("user-session");
    }

    [Fact]
    public async Task V2CallsRejectMissingUserSessionBeforeSendingRequest()
    {
        var handler = Handler("{}");
        var client = Client(handler, "host-token");
        var act = () => client.GetWorkspacesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("A Wickd user session is required*");
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task StrategyCallsRejectMissingWorkspaceBeforeSendingRequest()
    {
        var handler = Handler("{}");
        var client = Client(handler, "host-token", "user-session");
        var act = () => client.GetStrategiesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("A workspace is required*");
        handler.LastRequest.Should().BeNull();
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
    public async Task VwapRequestTransportsDecimalThresholdsAsExactInvariantStrings()
    {
        var handler = Handler("""{"schemaVersion":1,"contract":"vwap-analysis","provenance":{"generatedAtUtc":"2026-08-27T12:00:00Z","applicationVersion":"0.1.0","inspectionRunId":null,"sourceInspectionSchemaVersion":null},"marketId":"BTC_USDT_PERP","exchangeId":"binance","timeframe":"4h","fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-07-01T04:00:00Z","candleCount":1,"gapCount":0,"series":[],"levels":[],"classifications":[]}""");
        var client = Client(handler);
        const decimal threshold = 0.1234567890123456789012345678m;

        await client.AnalyzeVwapAsync(new VwapAnalysisRequest
        {
            Settings = new VwapAnalysisSettingsDto { MediumThreshold = threshold }
        });

        using var request = JsonDocument.Parse(handler.LastRequestBody!);
        request.RootElement.GetProperty("settings").GetProperty("mediumThreshold").GetString()
            .Should().Be("0.1234567890123456789012345678");
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

    private static WickdApiClient Client(MockHttpMessageHandler handler, string? token = null, string? sessionToken = null, string? workspaceId = null) =>
        new(
            new HttpClient(handler),
            new WickdCliConfig
            {
                ApiUrl = "https://api.wickdalgo.test",
                ApiToken = token,
                SessionToken = sessionToken,
                WorkspaceId = workspaceId
            });
}
