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

        [Description("Custom pivot strength for swing detection.")]
        [CommandOption("-p|--pivot-strength <N>")]
        public int? PivotStrength { get; init; }
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

        if (hasDataset && hasExplicitRange)
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

        var request = new BacktestRequest
        {
            DatasetAlias = settings.Dataset,
            MarketId = settings.Market ?? (hasDataset ? null : config.DefaultMarket),
            Timeframe = settings.Timeframe ?? (hasDataset ? null : config.DefaultTimeframe),
            FromUtc = fromUtc,
            ToUtc = toUtc,
            RunId = settings.RunId,
            PivotStrength = settings.PivotStrength ?? config.Structure.PivotStrength
        };

        var client = _apiClientFactory.CreateClient(settings);
        BacktestResultDto? result = null;

        try
        {
            var target = hasDataset ? $"dataset '{settings.Dataset}'" : $"{request.MarketId} ({request.Timeframe})";
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Running deterministic backtest for {target}...", async _ =>
                {
                    result = await client.RunBacktestAsync(request, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError("Failed to execute backtest.", ex);
            return ExitCodes.Error;
        }

        if (result == null)
        {
            _renderer.RenderError("No backtest result received from API server.");
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
}
