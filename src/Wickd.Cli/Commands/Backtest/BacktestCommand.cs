using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Backtest;

public class BacktestCommand : AsyncCommand<BacktestCommand.Settings>
{
    public class Settings : GlobalCommandSettings
    {
        [Description("Saved dataset alias name.")]
        [CommandOption("-d|--dataset <ALIAS>")]
        public string? Dataset { get; init; }

        [Description("Canonical market ID (e.g. BTC_USDT_PERP).")]
        [CommandOption("-m|--market <MARKET>")]
        public string? Market { get; init; }

        [Description("Candle timeframe (e.g. 5m, 1h, 4h, 1d).")]
        [CommandOption("-t|--timeframe <TIMEFRAME>")]
        public string? Timeframe { get; init; }

        [Description("Inclusive UTC start timestamp (ISO-8601).")]
        [CommandOption("--from <UTC>")]
        public string? From { get; init; }

        [Description("Exclusive UTC end timestamp (ISO-8601).")]
        [CommandOption("--to <UTC>")]
        public string? To { get; init; }

        [Description("Custom run ID (generated automatically if omitted).")]
        [CommandOption("-r|--run-id <ID>")]
        public string? RunId { get; init; }

    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public BacktestCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var hasDataset = !string.IsNullOrWhiteSpace(settings.Dataset);
        var hasExplicitRange = !string.IsNullOrWhiteSpace(settings.From) || !string.IsNullOrWhiteSpace(settings.To);
        var hasExplicitSelector = !string.IsNullOrWhiteSpace(settings.Market)
            || !string.IsNullOrWhiteSpace(settings.Timeframe)
            || hasExplicitRange;

        if (hasDataset && hasExplicitSelector)
        {
            _renderer.RenderError("Use either --dataset or explicit --market/--timeframe/--from/--to options, not both.");
            return ExitCodes.ValidationError;
        }

        if (!hasDataset && !hasExplicitRange)
        {
            _renderer.RenderError("Must specify either --dataset <alias> or explicit --from and --to timestamps.");
            return ExitCodes.ValidationError;
        }

        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;

        if (hasExplicitRange)
        {
            if (string.IsNullOrWhiteSpace(settings.From) || string.IsNullOrWhiteSpace(settings.To))
            {
                _renderer.RenderError("Both --from and --to UTC timestamps are required when using an explicit range.");
                return ExitCodes.ValidationError;
            }

            if (!DateTimeOffset.TryParse(settings.From, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var f))
            {
                _renderer.RenderError($"Invalid ISO-8601 UTC date for --from: {settings.From}");
                return ExitCodes.ValidationError;
            }

            if (!DateTimeOffset.TryParse(settings.To, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
            {
                _renderer.RenderError($"Invalid ISO-8601 UTC date for --to: {settings.To}");
                return ExitCodes.ValidationError;
            }

            if (f >= t)
            {
                _renderer.RenderError("--from timestamp must be strictly before --to timestamp.");
                return ExitCodes.ValidationError;
            }

            fromUtc = f;
            toUtc = t;
        }

        var config = _apiClientFactory.GetEffectiveConfig(settings);
        if (config.Structure.PivotStrength < 1)
        {
            _renderer.RenderError("Configured structure pivot strength must be at least one.");
            return ExitCodes.ValidationError;
        }

        var client = _apiClientFactory.CreateClient(settings);
        BacktestResultDto? result = null;
        var market = settings.Market ?? config.DefaultMarket;
        var timeframe = settings.Timeframe ?? config.DefaultTimeframe;
        var runId = string.IsNullOrWhiteSpace(settings.RunId)
            ? $"cli-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32]
            : settings.RunId;

        try
        {
            var target = hasDataset ? $"dataset '{settings.Dataset}'" : $"{market} ({timeframe})";
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Running structure engine for {target}...", async _ =>
                {
                    var selector = hasDataset
                        ? new CachedDatasetSelector { Alias = settings.Dataset }
                        : await ExplicitSelectorAsync(
                            client,
                            market,
                            config.DefaultExchange,
                            timeframe,
                            fromUtc!.Value,
                            toUtc!.Value,
                            cancellationToken);
                    var request = new BacktestRequest
                    {
                        RunId = runId,
                        Dataset = selector,
                        PivotStrength = config.Structure.PivotStrength
                    };
                    result = await client.RunBacktestAsync(request, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError("Failed to execute structure run.", ex);
            return ExitCodes.Error;
        }

        if (result == null)
        {
            _renderer.RenderError("No structure-run result received from API server.");
            return ExitCodes.Error;
        }

        if (settings.Json)
        {
            _renderer.RenderJson(result);
        }
        else
        {
            _renderer.RenderBacktestResult(result);
        }

        return ExitCodes.Success;
    }

    private static async Task<CachedDatasetSelector> ExplicitSelectorAsync(
        IWickdApiClient client,
        string market,
        string exchange,
        string timeframe,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var instrument = await SupportedInstrumentResolver.ResolveAsync(
            client, market, exchange, timeframe, cancellationToken);
        return new CachedDatasetSelector
        {
            MarketId = market,
            ExchangeId = exchange,
            ExchangeSymbol = instrument.ExchangeSymbol,
            Timeframe = timeframe,
            FromUtc = fromUtc,
            ToUtc = toUtc
        };
    }
}
