using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Tests;

public class CommandRegistrationTests
{
    [Fact]
    public async Task CommandAppRegistersRunAndNotBacktest()
    {
        var help = await CaptureAsync(app => app.RunAsync(["--help"]));

        help.ExitCode.Should().Be(0);
        CommandNames(help.Output).Should().Contain("run");
        CommandNames(help.Output).Should().NotContain("backtest");
        help.Output.Should().Contain("structure engine");
        help.Output.Should().NotContain("strategy");
    }

    [Fact]
    public async Task RunHelpDescribesStructureSelectors()
    {
        var help = await CaptureAsync(app => app.RunAsync(["run", "--help"]));

        help.ExitCode.Should().Be(0);
        help.Output.Should().Contain("--dataset");
        help.Output.Should().Contain("--market");
        help.Output.Should().Contain("--timeframe");
        help.Output.Should().Contain("--from");
        help.Output.Should().Contain("--to");
        help.Output.Should().Contain("--run-id");
        help.Output.Should().NotContain("order");
        help.Output.Should().NotContain("fill");
        help.Output.Should().NotContain("RR");
    }

    [Fact]
    public async Task RunDatasetAliasSendsTheStructurePipelineRequest()
    {
        var factory = new FakeApiClientFactory();
        var registrar = CreateRegistrar(factory);
        var app = Program.CreateCommandApp(registrar);

        var exitCode = await app.RunAsync(["run", "--dataset", "jul-btc", "--run-id", "jul-btc-smoke"]);

        exitCode.Should().Be(ExitCodes.Success);
        factory.Client.BacktestRequest.Should().NotBeNull();
        factory.Client.BacktestRequest!.Dataset!.Alias.Should().Be("jul-btc");
        factory.Client.BacktestRequest.Candles.Should().BeNull();
        factory.Client.BacktestRequest.RunId.Should().Be("jul-btc-smoke");
        factory.Client.BacktestRequest.PivotStrength.Should().Be(2);
    }

    [Fact]
    public async Task BacktestIsNotARegisteredCommand()
    {
        var factory = new FakeApiClientFactory();
        var help = await CaptureAsync(app => app.RunAsync(["backtest", "--help"]), factory);
        var run = await CaptureAsync(app => app.RunAsync(["backtest", "--dataset", "jul-btc"]), factory);

        help.ExitCode.Should().NotBe(0);
        run.ExitCode.Should().NotBe(0);
        factory.Client.BacktestRequest.Should().BeNull();
        Combined(help, run).Should().NotContain("structure engine over a cached dataset");
    }

    private static ITypeRegistrar CreateRegistrar(FakeApiClientFactory factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfigManager, ConfigManager>();
        services.AddSingleton<IConsoleRenderer, SilentConsoleRenderer>();
        services.AddSingleton<IApiClientFactory>(factory);
        services.AddHttpClient("WickdApi");
        return new TypeRegistrar(services);
    }

    private static async Task<CapturedRun> CaptureAsync(
        Func<CommandApp, Task<int>> invoke,
        FakeApiClientFactory? factory = null)
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output)
        });
        var app = Program.CreateCommandApp(factory is null ? null : CreateRegistrar(factory), console);
        var exitCode = await invoke(app);
        return new CapturedRun(exitCode, output.ToString());
    }

    private static IEnumerable<string> CommandNames(string help)
    {
        var inCommands = false;
        foreach (var raw in help.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Trim().Equals("COMMANDS:", StringComparison.OrdinalIgnoreCase) ||
                line.Trim().Equals("Commands:", StringComparison.OrdinalIgnoreCase))
            {
                inCommands = true;
                continue;
            }

            if (!inCommands)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                yield break;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                yield break;
            }

            var name = line.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            yield return name;
        }
    }

    private static string Combined(CapturedRun first, CapturedRun second) => first.Output + second.Output;

    private readonly record struct CapturedRun(int ExitCode, string Output);

    private sealed class SilentConsoleRenderer : IConsoleRenderer
    {
        public void RenderBanner() { }
        public void RenderSuccess(string message) { }
        public void RenderError(string message, Exception? ex = null) { }
        public void RenderWarning(string message) { }
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

    private sealed class FakeApiClientFactory : IApiClientFactory
    {
        public FakeApiClient Client { get; } = new();

        public IWickdApiClient CreateClient(GlobalCommandSettings settings) => Client;

        public WickdCliConfig GetEffectiveConfig(GlobalCommandSettings settings) => new();
    }

    private sealed class FakeApiClient : IWickdApiClient
    {
        public BacktestRequest? BacktestRequest { get; private set; }

        public Task<FetchResultDto> FetchCandlesAsync(FetchHistoricalCandlesRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FetchResultDto());

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

        public Task<BacktestResultDto> RunBacktestAsync(BacktestRequest request, CancellationToken cancellationToken = default)
        {
            BacktestRequest = request;
            return Task.FromResult(new BacktestResultDto { RunId = request.RunId });
        }

        public Task<VwapAnalysisResultDto> AnalyzeVwapAsync(VwapAnalysisRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VwapAnalysisResultDto());

        public Task<List<DatasetAliasDto>> GetDatasetAliasesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<DatasetAliasDto>());

        public Task<DatasetAliasDto> SaveDatasetAliasAsync(SaveDatasetAliasRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DatasetAliasDto { Alias = request.Alias });

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
    }
}
