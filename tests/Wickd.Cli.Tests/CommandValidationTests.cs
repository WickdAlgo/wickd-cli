using FluentAssertions;
using Wickd.Cli.Commands.Backtest;
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
        public IWickdApiClient CreateClient(Commands.Settings.GlobalCommandSettings settings)
        {
            return new FakeApiClient();
        }

        public WickdCliConfig GetEffectiveConfig(Commands.Settings.GlobalCommandSettings settings)
        {
            return new WickdCliConfig();
        }
    }

    private class FakeApiClient : IWickdApiClient
    {
        public Task<FetchResultDto> FetchCandlesAsync(FetchHistoricalCandlesRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FetchResultDto());

        public Task<BacktestResultDto> RunBacktestAsync(BacktestRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BacktestResultDto());

        public Task<VwapAnalysisResultDto> AnalyzeVwapAsync(VwapAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VwapAnalysisResultDto());

        public Task<List<DatasetAliasDto>> GetDatasetAliasesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<DatasetAliasDto>());

        public Task<DatasetAliasDto> SaveDatasetAliasAsync(SaveDatasetAliasRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DatasetAliasDto());

        public Task<bool> DeleteDatasetAliasAsync(string alias, bool deleteCache = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<List<RunListingDto>> GetRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<RunListingDto>());

        public Task<InspectionRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InspectionRunDto?>(new InspectionRunDto { RunId = runId });

        public Task<bool> DeleteRunAsync(string runId, bool force = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<List<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<AccountDto>());

        public Task<AccountRiskDto?> GetAccountRiskAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountRiskDto?>(new AccountRiskDto { AccountId = accountId });

        public Task<List<TradeSummaryDto>> GetTradesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<TradeSummaryDto>());

        public Task<TradeDetailDto?> GetTradeAsync(string tradeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TradeDetailDto?>(new TradeDetailDto { TradeId = tradeId });

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
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

    private sealed class TestManageRunsDeleteCommand : ManageRunsDeleteCommand
    {
        public TestManageRunsDeleteCommand(IApiClientFactory factory, IConsoleRenderer renderer) : base(factory, renderer) { }
        public Task<int> RunAsync(ManageRunsDeleteCommand.Settings settings, CancellationToken ct = default) => ExecuteAsync(null!, settings, ct);
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
    public async Task ManageRunsDeleteCommand_WithoutForce_ReturnsValidationError()
    {
        var renderer = new FakeConsoleRenderer();
        var factory = new FakeApiClientFactory();
        var cmd = new TestManageRunsDeleteCommand(factory, renderer);

        var settings = new ManageRunsDeleteCommand.Settings
        {
            RunId = "my-run",
            Force = false
        };

        var exitCode = await cmd.RunAsync(settings);

        exitCode.Should().Be(ExitCodes.ValidationError);
        renderer.Errors.Should().Contain(e => e.Contains("--force"));
    }
}
