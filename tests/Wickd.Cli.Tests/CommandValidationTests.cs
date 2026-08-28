using FluentAssertions;
using Wickd.Cli.Commands.Backtest;
using Wickd.Cli.Commands.Analyze;
using Wickd.Cli.Commands.Fetch;
using Wickd.Cli.Commands.Manage;
using Wickd.Cli.Common;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;
using Xunit;

namespace Wickd.Cli.Tests;

public class CommandValidationTests
{
    private class FakeConsoleRenderer : IConsoleRenderer
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> Successes { get; } = [];

        public void RenderBanner() { }
        public void RenderSuccess(string message) => Successes.Add(message);
        public void RenderError(string message, Exception? ex = null) => Errors.Add(message);
        public void RenderWarning(string message) => Warnings.Add(message);
        public void RenderInfo(string message) { }
        public void RenderJson<T>(T data) { }
        public void RenderDatasets(IEnumerable<DatasetAliasDto> datasets) { }
        public void RenderRuns(IEnumerable<RunListingDto> runs) { }
        public void RenderTrades(IEnumerable<TradeSummaryDto> trades) { }
        public void RenderAccounts(IEnumerable<AccountDto> accounts) { }
        public void RenderAccountRisk(AccountRiskDto risk) { }
        public void RenderVwapAnalysis(VwapAnalysisResultDto result) { }
        public void RenderBacktestResult(BacktestResultDto result) { }
        public void RenderFetchResult(FetchResultDto result) { }
    }

    private class FakeApiClientFactory : IApiClientFactory
    {
        public FakeApiClient Client { get; } = new();

        public IWickdApiClient CreateClient(Commands.Settings.GlobalCommandSettings settings)
        {
            return Client;
        }

        public WickdCliConfig GetEffectiveConfig(Commands.Settings.GlobalCommandSettings settings)
        {
            return new WickdCliConfig();
        }
    }

    private class FakeApiClient : IWickdApiClient
    {
        public FetchHistoricalCandlesRequest? FetchRequest { get; private set; }
        public SaveDatasetAliasRequest? AliasRequest { get; private set; }
        public BacktestRequest? BacktestRequest { get; private set; }
        public VwapAnalysisRequest? VwapRequest { get; private set; }

        public Task<FetchResultDto> FetchCandlesAsync(FetchHistoricalCandlesRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaptureFetch(request));

        public Task<SupportedInstrumentsPayload> GetSupportedInstrumentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SupportedInstrumentsPayload
            {
                SchemaVersion = 1,
                Contract = "supported-instruments",
                Timeframes = ["4h"],
                Instruments =
                [
                    new SupportedInstrumentDto
                    {
                        MarketId = "BTC_USDT_PERP",
                        ExchangeId = "binance",
                        ExchangeSymbol = "BTC/USDT:USDT"
                    }
                ]
            });

        public Task<BacktestResultDto> RunBacktestAsync(BacktestRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaptureBacktest(request));

        public Task<VwapAnalysisResultDto> AnalyzeVwapAsync(VwapAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaptureVwap(request));

        public Task<List<DatasetAliasDto>> GetDatasetAliasesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<DatasetAliasDto>());

        public Task<DatasetAliasDto> SaveDatasetAliasAsync(SaveDatasetAliasRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(CaptureAlias(request));

        public Task<bool> DeleteDatasetAliasAsync(string alias, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<List<RunListingDto>> GetRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<RunListingDto>());

        public Task<InspectionRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InspectionRunDto?>(new InspectionRunDto());


        public Task<AccountsPayloadDto> GetAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountsPayloadDto());

        public Task<AccountRiskDto?> GetAccountRiskAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountRiskDto?>(new AccountRiskDto { AccountId = accountId });

        public Task<List<TradeSummaryDto>> GetTradesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<TradeSummaryDto>());

        public Task<TradeDetailDto?> GetTradeAsync(string tradeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TradeDetailDto?>(new TradeDetailDto());

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        private FetchResultDto CaptureFetch(FetchHistoricalCandlesRequest request)
        {
            FetchRequest = request;
            return new FetchResultDto
            {
                MarketId = request.MarketId,
                ExchangeSymbol = request.ExchangeSymbol,
                Timeframe = request.Timeframe,
                FromUtc = request.FromUtc,
                ToUtc = request.ToUtc
            };
        }

        private DatasetAliasDto CaptureAlias(SaveDatasetAliasRequest request)
        {
            AliasRequest = request;
            return new DatasetAliasDto { Alias = request.Alias };
        }

        private BacktestResultDto CaptureBacktest(BacktestRequest request)
        {
            BacktestRequest = request;
            return new BacktestResultDto { RunId = request.RunId };
        }

        private VwapAnalysisResultDto CaptureVwap(VwapAnalysisRequest request)
        {
            VwapRequest = request;
            return new VwapAnalysisResultDto();
        }
    }

    private sealed class TestBacktestCommand : BacktestCommand
    {
        public TestBacktestCommand(IApiClientFactory factory, IConsoleRenderer renderer) : base(factory, renderer) { }
        public Task<int> RunAsync(BacktestCommand.Settings settings, CancellationToken ct = default) => ExecuteAsync(null!, settings, ct);
    }

    private sealed class TestFetchCommand : FetchCommand
    {
        public TestFetchCommand(IApiClientFactory factory, IConsoleRenderer renderer) : base(factory, renderer) { }
        public Task<int> RunAsync(FetchCommand.Settings settings, CancellationToken ct = default) => ExecuteAsync(null!, settings, ct);
    }

    private sealed class TestAnalyzeVwapCommand : AnalyzeVwapCommand
    {
        public TestAnalyzeVwapCommand(IApiClientFactory factory, IConsoleRenderer renderer) : base(factory, renderer) { }
        public Task<int> RunAsync(AnalyzeVwapCommand.Settings settings, CancellationToken ct = default) => ExecuteAsync(null!, settings, ct);
    }


    [Fact]
    public async Task BacktestCommand_WithBothDatasetAndExplicitRange_ReturnsValidationError()
    {
        var renderer = new FakeConsoleRenderer();
        var factory = new FakeApiClientFactory();
        var cmd = new TestBacktestCommand(factory, renderer);

        var settings = new BacktestCommand.Settings
        {
            Dataset = "jul-btc",
            From = "2026-07-01T00:00:00Z",
            To = "2026-08-01T00:00:00Z"
        };

        var exitCode = await cmd.RunAsync(settings);

        exitCode.Should().Be(ExitCodes.ValidationError);
        renderer.Errors.Should().Contain(e => e.Contains("not both"));
    }

    [Fact]
    public async Task BacktestCommand_WithDatasetAndMarket_ReturnsValidationErrorBeforeCallingTheApi()
    {
        var renderer = new FakeConsoleRenderer();
        var factory = new FakeApiClientFactory();
        var command = new TestBacktestCommand(factory, renderer);

        var exitCode = await command.RunAsync(new BacktestCommand.Settings
        {
            Dataset = "jul-btc",
            Market = "BTC_USDT_PERP"
        });

        exitCode.Should().Be(ExitCodes.ValidationError);
        factory.Client.BacktestRequest.Should().BeNull();
        renderer.Errors.Should().Contain(error => error.Contains("not both"));
    }

    [Fact]
    public async Task AnalyzeCommand_WithDatasetAndTimeframe_ReturnsValidationErrorBeforeCallingTheApi()
    {
        var renderer = new FakeConsoleRenderer();
        var factory = new FakeApiClientFactory();
        var command = new TestAnalyzeVwapCommand(factory, renderer);

        var exitCode = await command.RunAsync(new AnalyzeVwapCommand.Settings
        {
            Dataset = "jul-btc",
            Timeframe = "4h"
        });

        exitCode.Should().Be(ExitCodes.ValidationError);
        factory.Client.VwapRequest.Should().BeNull();
        renderer.Errors.Should().Contain(error => error.Contains("not both"));
    }

    [Fact]
    public async Task BacktestCommand_WithNeitherDatasetNorExplicitRange_ReturnsValidationError()
    {
        var renderer = new FakeConsoleRenderer();
        var factory = new FakeApiClientFactory();
        var cmd = new TestBacktestCommand(factory, renderer);

        var settings = new BacktestCommand.Settings();

        var exitCode = await cmd.RunAsync(settings);

        exitCode.Should().Be(ExitCodes.ValidationError);
        renderer.Errors.Should().Contain(e => e.Contains("Must specify either"));
    }

    [Fact]
    public async Task FetchCommand_WithMissingTimestamps_ReturnsValidationError()
    {
        var renderer = new FakeConsoleRenderer();
        var factory = new FakeApiClientFactory();
        var cmd = new TestFetchCommand(factory, renderer);

        var settings = new FetchCommand.Settings
        {
            Market = "BTC_USDT_PERP"
        };

        var exitCode = await cmd.RunAsync(settings);

        exitCode.Should().Be(ExitCodes.ValidationError);
        renderer.Errors.Should().Contain(e => e.Contains("Both --from and --to"));
    }

    [Fact]
    public async Task FetchResolvesExchangeSymbolAndSavesAliasAfterFetch()
    {
        var renderer = new FakeConsoleRenderer();
        var factory = new FakeApiClientFactory();
        var command = new TestFetchCommand(factory, renderer);

        var exitCode = await command.RunAsync(new FetchCommand.Settings
        {
            Market = "BTC_USDT_PERP",
            Exchange = "binance",
            Timeframe = "4h",
            From = "2026-07-01T00:00:00Z",
            To = "2026-08-01T00:00:00Z",
            Alias = "jul-btc",
            Force = true
        });

        exitCode.Should().Be(ExitCodes.Success);
        factory.Client.FetchRequest!.ExchangeSymbol.Should().Be("BTC/USDT:USDT");
        factory.Client.AliasRequest!.Alias.Should().Be("jul-btc");
        factory.Client.AliasRequest.Force.Should().BeTrue();
    }

    [Fact]
    public async Task BacktestAliasNeverCarriesCandles()
    {
        var factory = new FakeApiClientFactory();
        var command = new TestBacktestCommand(factory, new FakeConsoleRenderer());

        var exitCode = await command.RunAsync(new BacktestCommand.Settings
        {
            Dataset = "jul-btc",
            RunId = "jul-btc-smoke"
        });

        exitCode.Should().Be(ExitCodes.Success);
        factory.Client.BacktestRequest!.Dataset!.Alias.Should().Be("jul-btc");
        factory.Client.BacktestRequest.Candles.Should().BeNull();
        factory.Client.BacktestRequest.PivotStrength.Should().Be(2);
    }

    [Fact]
    public async Task AnalyzeExplicitRangeSendsTheCompleteCacheIdentity()
    {
        var factory = new FakeApiClientFactory();
        var command = new TestAnalyzeVwapCommand(factory, new FakeConsoleRenderer());

        var exitCode = await command.RunAsync(new AnalyzeVwapCommand.Settings
        {
            Market = "BTC_USDT_PERP",
            Timeframe = "4h",
            From = "2026-07-01T00:00:00Z",
            To = "2026-08-01T00:00:00Z"
        });

        exitCode.Should().Be(ExitCodes.Success);
        factory.Client.VwapRequest!.Dataset!.ExchangeId.Should().Be("binance");
        factory.Client.VwapRequest.Dataset.ExchangeSymbol.Should().Be("BTC/USDT:USDT");
        factory.Client.VwapRequest.Candles.Should().BeNull();
        factory.Client.VwapRequest.Settings!.EnabledPeriods.Should().Equal("daily", "weekly");
        factory.Client.VwapRequest.Settings.PreviousLevelPeriods.Should().Equal("daily", "weekly");
    }

    [Fact]
    public async Task AnalyzePeriodOverridesPreserveNoneAsAnEmptyLevelSet()
    {
        var factory = new FakeApiClientFactory();
        var command = new TestAnalyzeVwapCommand(factory, new FakeConsoleRenderer());

        var exitCode = await command.RunAsync(new AnalyzeVwapCommand.Settings
        {
            Dataset = "jul-btc",
            Periods = "daily,monthly",
            LevelPeriods = "NoNe"
        });

        exitCode.Should().Be(ExitCodes.Success);
        factory.Client.VwapRequest!.Settings!.EnabledPeriods.Should().Equal("daily", "monthly");
        factory.Client.VwapRequest.Settings.PreviousLevelPeriods.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeRejectsAnUnsupportedPeriodBeforeCallingTheApi()
    {
        var renderer = new FakeConsoleRenderer();
        var factory = new FakeApiClientFactory();
        var command = new TestAnalyzeVwapCommand(factory, renderer);

        var exitCode = await command.RunAsync(new AnalyzeVwapCommand.Settings
        {
            Dataset = "jul-btc",
            Periods = "daily,hourly"
        });

        exitCode.Should().Be(ExitCodes.ValidationError);
        factory.Client.VwapRequest.Should().BeNull();
        renderer.Errors.Should().Contain(error => error.Contains("hourly"));
    }
}
