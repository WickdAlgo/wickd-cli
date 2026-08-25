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

public sealed class AnalyzeVwapCommand : AsyncCommand<AnalyzeVwapCommand.Settings>
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

        [Description("Comma-separated anchor periods producing running VWAP (e.g. daily,weekly,monthly,quarterly,yearly).")]
        [CommandOption("--periods <LIST>")]
        public string? Periods { get; init; }

        [Description("Comma-separated anchor periods producing previous-close levels, or 'none'.")]
        [CommandOption("--level-periods <LIST>")]
        public string? LevelPeriods { get; init; }

        [Description("Export full result records to a JSONL file.")]
        [CommandOption("-o|--out <PATH>")]
        public string? OutPath { get; init; }
    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    private static readonly JsonSerializerOptions JsonLineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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

        List<string>? periods = null;
        if (!string.IsNullOrWhiteSpace(settings.Periods))
        {
            periods = settings.Periods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        List<string>? levelPeriods = null;
        if (!string.IsNullOrWhiteSpace(settings.LevelPeriods))
        {
            levelPeriods = settings.LevelPeriods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        var request = new VwapAnalysisRequest
        {
            DatasetAlias = settings.Dataset,
            MarketId = settings.Market ?? (hasDataset ? null : config.DefaultMarket),
            Timeframe = settings.Timeframe ?? (hasDataset ? null : config.DefaultTimeframe),
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Periods = periods ?? config.Vwap.EnabledPeriods,
            LevelPeriods = levelPeriods ?? config.Vwap.PreviousLevelPeriods
        };

        var client = _apiClientFactory.CreateClient(settings);
        VwapAnalysisResultDto? result = null;

        try
        {
            var target = hasDataset ? $"dataset '{settings.Dataset}'" : $"{request.MarketId} ({request.Timeframe})";
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Running VWAP & volume analysis for {target}...", async _ =>
                {
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

                foreach (var lvl in result.Levels)
                {
                    writer.WriteLine(JsonSerializer.Serialize(lvl, JsonLineOptions));
                }

                foreach (var cls in result.Classifications)
                {
                    writer.WriteLine(JsonSerializer.Serialize(cls, JsonLineOptions));
                }

                foreach (var pt in result.Points)
                {
                    writer.WriteLine(JsonSerializer.Serialize(pt, JsonLineOptions));
                }

                _renderer.RenderSuccess($"Exported {result.Levels.Count + result.Classifications.Count + result.Points.Count} records to [cyan]{fullOutPath}[/]");
            }
            catch (Exception ex)
            {
                _renderer.RenderError($"Failed to write JSONL export to '{settings.OutPath}'.", ex);
                return ExitCodes.Error;
            }
        }

        return ExitCodes.Success;
    }
}
