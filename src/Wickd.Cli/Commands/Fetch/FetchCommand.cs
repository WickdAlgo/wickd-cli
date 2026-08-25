using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Fetch;

public class FetchCommand : AsyncCommand<FetchCommand.Settings>
{
    public class Settings : GlobalCommandSettings
    {
        [Description("Canonical market ID (e.g. BTC_USDT_PERP).")]
        [CommandOption("-m|--market <MARKET>")]
        public string? Market { get; init; }

        [Description("Candle timeframe (e.g. 5m, 1h, 4h, 1d).")]
        [CommandOption("-t|--timeframe <TIMEFRAME>")]
        public string? Timeframe { get; init; }

        [Description("Inclusive UTC start timestamp (ISO-8601, e.g. 2026-07-01T00:00:00Z).")]
        [CommandOption("--from <UTC>")]
        public string? From { get; init; }

        [Description("Exclusive UTC end timestamp (ISO-8601, e.g. 2026-08-02T08:00:00Z).")]
        [CommandOption("--to <UTC>")]
        public string? To { get; init; }

        [Description("Target exchange ID (e.g. binance, bybit). Defaults to config.")]
        [CommandOption("-e|--exchange <EXCHANGE>")]
        public string? Exchange { get; init; }

        [Description("Save range under a friendly alias name.")]
        [CommandOption("-a|--alias <NAME>")]
        public string? Alias { get; init; }

        [Description("Overwrite existing alias of the same name.")]
        [CommandOption("-f|--force")]
        public bool Force { get; init; }
    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public FetchCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = _apiClientFactory.GetEffectiveConfig(settings);

        var market = settings.Market ?? config.DefaultMarket;
        var timeframe = settings.Timeframe ?? config.DefaultTimeframe;
        var exchange = settings.Exchange ?? config.DefaultExchange;

        if (string.IsNullOrWhiteSpace(settings.From) || string.IsNullOrWhiteSpace(settings.To))
        {
            _renderer.RenderError("Both --from and --to UTC timestamps are required for fetching candles.");
            return ExitCodes.ValidationError;
        }

        if (!DateTimeOffset.TryParse(settings.From, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var fromUtc))
        {
            _renderer.RenderError($"Invalid ISO-8601 UTC date for --from: {settings.From}");
            return ExitCodes.ValidationError;
        }

        if (!DateTimeOffset.TryParse(settings.To, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var toUtc))
        {
            _renderer.RenderError($"Invalid ISO-8601 UTC date for --to: {settings.To}");
            return ExitCodes.ValidationError;
        }

        if (fromUtc >= toUtc)
        {
            _renderer.RenderError("--from timestamp must be strictly before --to timestamp.");
            return ExitCodes.ValidationError;
        }

        var client = _apiClientFactory.CreateClient(settings);
        FetchResultDto? result = null;

        var request = new FetchHistoricalCandlesRequest
        {
            MarketId = market,
            Timeframe = timeframe,
            ExchangeId = exchange,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Alias = settings.Alias,
            Force = settings.Force
        };

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching candles for {market} ({timeframe}) from {exchange}...", async _ =>
                {
                    result = await client.FetchCandlesAsync(request, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError("Failed to fetch historical candles.", ex);
            return ExitCodes.Error;
        }

        if (result == null)
        {
            _renderer.RenderError("No result received from API server.");
            return ExitCodes.Error;
        }

        if (settings.Json)
        {
            _renderer.RenderJson(result);
        }
        else
        {
            _renderer.RenderFetchResult(result);
        }

        return ExitCodes.Success;
    }
}
