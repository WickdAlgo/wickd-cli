using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Analyze;

public class AnalyzeVwapCommand : AsyncCommand<AnalyzeVwapCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
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

        [Description("Comma-separated anchor periods producing running VWAP.")]
        [CommandOption("--periods <LIST>")]
        public string? Periods { get; init; }

        [Description("Comma-separated periods producing previous-close levels, or 'none'.")]
        [CommandOption("--level-periods <LIST>")]
        public string? LevelPeriods { get; init; }

        [Description("Export full result records to a JSONL file.")]
        [CommandOption("-o|--out <PATH>")]
        public string? OutPath { get; init; }
    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    internal static readonly JsonSerializerOptions JsonLineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new DecimalStringJsonConverter(),
            new NullableDecimalStringJsonConverter()
        }
    };

    public AnalyzeVwapCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
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
        if (config.Vwap.VolumeLength < 2)
        {
            _renderer.RenderError("Configured VWAP volume length must be at least two.");
            return ExitCodes.ValidationError;
        }

        if (config.Vwap.LargeThreshold < config.Vwap.MediumThreshold)
        {
            _renderer.RenderError("Configured large volume threshold must be at least the medium threshold.");
            return ExitCodes.ValidationError;
        }

        if (!TryResolvePeriods(
                settings.Periods,
                config.Vwap.EnabledPeriods,
                allowNone: false,
                "--periods",
                out var enabledPeriods)
            || !TryResolvePeriods(
                settings.LevelPeriods,
                config.Vwap.PreviousLevelPeriods,
                allowNone: true,
                "--level-periods",
                out var previousLevelPeriods))
        {
            return ExitCodes.ValidationError;
        }

        var client = _apiClientFactory.CreateClient(settings);
        VwapAnalysisResultDto? result = null;
        var market = settings.Market ?? config.DefaultMarket;
        var timeframe = settings.Timeframe ?? config.DefaultTimeframe;

        try
        {
            var target = hasDataset ? $"dataset '{settings.Dataset}'" : $"{market} ({timeframe})";
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Running VWAP & volume analysis for {target}...", async _ =>
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
                    var request = new VwapAnalysisRequest
                    {
                        Dataset = selector,
                        Settings = new VwapAnalysisSettingsDto
                        {
                            EnabledPeriods = enabledPeriods,
                            PreviousLevelPeriods = previousLevelPeriods,
                            VolumeLength = config.Vwap.VolumeLength,
                            MediumThreshold = config.Vwap.MediumThreshold,
                            LargeThreshold = config.Vwap.LargeThreshold,
                            LowVolumeThreshold = config.Vwap.LowVolumeThreshold,
                            ShowLowVolume = config.Vwap.ShowLowVolume
                        }
                    };
                    result = await client.AnalyzeVwapAsync(request, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError("Failed to execute VWAP analysis.", ex);
            return ExitCodes.Error;
        }

        if (result == null)
        {
            _renderer.RenderError("No analysis result received from API server.");
            return ExitCodes.Error;
        }

        if (settings.Json)
        {
            _renderer.RenderJson(result);
        }
        else
        {
            _renderer.RenderVwapAnalysis(result);
        }

        if (!string.IsNullOrWhiteSpace(settings.OutPath))
        {
            try
            {
                var fullOutPath = Path.GetFullPath(settings.OutPath);
                var outDir = Path.GetDirectoryName(fullOutPath);
                if (!string.IsNullOrWhiteSpace(outDir) && !Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                using var writer = new StreamWriter(fullOutPath, false);

                foreach (var series in result.Series)
                {
                    foreach (var point in series.Points)
                    {
                        writer.WriteLine(JsonSerializer.Serialize(
                            new { kind = "point", series.Period, point.OpenTimeUtc, point.RunningVwap, point.PreviousClose },
                            JsonLineOptions));
                    }
                }

                foreach (var level in result.Levels)
                {
                    writer.WriteLine(JsonSerializer.Serialize(
                        new { kind = "level", level.Period, level.Price, level.BornAtUtc, level.ExpiresAtUtc, level.SweptAtUtc },
                        JsonLineOptions));
                }

                foreach (var classification in result.Classifications)
                {
                    writer.WriteLine(JsonSerializer.Serialize(
                        new { kind = "classification", classification.OpenTimeUtc, classification.VolumeClass, classification.IsUp, classification.Score },
                        JsonLineOptions));
                }

                var pointCount = result.Series.Sum(series => series.Points.Count);
                _renderer.RenderSuccess($"Exported {result.Levels.Count + result.Classifications.Count + pointCount} records to [cyan]{fullOutPath}[/]");
            }
            catch (Exception ex)
            {
                _renderer.RenderError($"Failed to write JSONL export to '{settings.OutPath}'.", ex);
                return ExitCodes.Error;
            }
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

    private bool TryResolvePeriods(
        string? value,
        IReadOnlyList<string> configured,
        bool allowNone,
        string option,
        out List<string> periods)
    {
        periods = [];
        IReadOnlyList<string> raw = value is null
            ? configured
            : value.Split(',', StringSplitOptions.TrimEntries);
        if (raw.Count == 0 || raw.Any(string.IsNullOrWhiteSpace))
        {
            _renderer.RenderError($"{option} must name at least one VWAP anchor period.");
            return false;
        }

        if (raw.Any(item => item.Equals("none", StringComparison.OrdinalIgnoreCase)))
        {
            if (!allowNone || raw.Count != 1)
            {
                _renderer.RenderError($"{option} must use 'none' by itself, and only level periods support it.");
                return false;
            }

            return true;
        }

        string[] supported = ["daily", "weekly", "monthly", "quarterly", "yearly"];
        foreach (var item in raw)
        {
            var normalized = item.ToLowerInvariant();
            if (!supported.Contains(normalized, StringComparer.Ordinal))
            {
                _renderer.RenderError(
                    $"Unsupported VWAP anchor period '{item}'. Supported periods: {string.Join(", ", supported)}.");
                return false;
            }

            if (!periods.Contains(normalized, StringComparer.Ordinal))
            {
                periods.Add(normalized);
            }
        }

        return true;
    }
}
