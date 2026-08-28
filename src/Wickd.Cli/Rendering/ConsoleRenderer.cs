using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Rendering;

public sealed class ConsoleRenderer : IConsoleRenderer
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void RenderBanner()
    {
        AnsiConsole.Write(
            new FigletText("WickdAlgo")
                .LeftJustified()
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[grey]Deterministic Backtest & Algorithmic Trading CLI[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
    }

    public void RenderError(string message, Exception? ex = null)
    {
        AnsiConsole.MarkupLine($"[red]✗ Error:[/] {Markup.Escape(message)}");
        if (ex != null)
        {
            AnsiConsole.MarkupLine($"[dim red]{Markup.Escape(ex.Message)}[/]");
        }
    }

    public void RenderWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(message)}");
    }

    public void RenderInfo(string message)
    {
        AnsiConsole.MarkupLine($"[blue]ℹ[/] {Markup.Escape(message)}");
    }

    public void RenderJson<T>(T data)
    {
        var json = JsonSerializer.Serialize(data, IndentedJsonOptions);
        AnsiConsole.Write(new JsonText(json));
        AnsiConsole.WriteLine();
    }

    public void RenderFetchResult(FetchResultDto result)
    {
        var panel = new Panel(
            new Rows(
                new Markup($"[bold]Market:[/] [cyan]{result.MarketId}[/]"),
                new Markup($"[bold]Timeframe:[/] [yellow]{result.Timeframe}[/]"),
                new Markup($"[bold]Range:[/] {result.FromUtc:yyyy-MM-dd HH:mm:ss}Z -> {result.ToUtc:yyyy-MM-dd HH:mm:ss}Z"),
                new Markup($"[bold]Candles:[/] [green]{result.CandleCount:N0}[/]"),
                new Markup($"[bold]Cache:[/] {(result.CacheHit ? "[green]hit[/]" : "[yellow]filled[/]")}"),
                new Markup($"[bold]Exchange symbol:[/] {Markup.Escape(result.ExchangeSymbol)}")
            )
        )
        {
            Header = new PanelHeader("[bold green]Fetch Summary[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
    }

    public void RenderBacktestResult(BacktestResultDto result)
    {
        var panel = new Panel(
            new Rows(
                new Markup($"[bold]Run ID:[/] [cyan]{result.RunId}[/]"),
                new Markup($"[bold]Market:[/] {result.MarketId} ({result.Timeframe})"),
                new Markup($"[bold]Range:[/] {result.FromUtc:yyyy-MM-dd HH:mm:ss}Z -> {result.ToUtc:yyyy-MM-dd HH:mm:ss}Z"),
                new Markup($"[bold]Candles Replayed:[/] [green]{result.CandleCount:N0}[/]"),
                new Markup($"[bold]Events:[/] [yellow]{result.EventCount:N0}[/]"),
                new Markup($"[bold]Gaps:[/] {(result.GapCount == 0 ? "[green]0[/]" : $"[yellow]{result.GapCount:N0}[/]")}")
            )
        )
        {
            Header = new PanelHeader("[bold green]Backtest Run Completed[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);

    }

    public void RenderVwapAnalysis(VwapAnalysisResultDto result)
    {
        AnsiConsole.MarkupLine(
            $"Analyzed [bold green]{result.CandleCount}[/] candles for [bold cyan]{result.MarketId}[/] "
            + $"[yellow]{result.Timeframe}[/] from {result.FromUtc:yyyy-MM-ddTHH:mm:ssZ} "
            + $"to {result.ToUtc:yyyy-MM-ddTHH:mm:ssZ} (gaps {result.GapCount}).");

        if (result.Series.Count > 0)
        {
            var table = new Table().RoundedBorder();
            table.AddColumn("Period");
            table.AddColumn("Now (Running VWAP)");
            table.AddColumn("Previous Close");
            table.AddColumn("Levels");

            foreach (var series in result.Series)
            {
                var latest = series.Points.LastOrDefault();
                var levels = result.Levels.Where(level => level.Period == series.Period).ToList();
                var swept = levels.Count(level => level.SweptAtUtc is not null);
                var prevText = latest?.PreviousClose is not null
                    ? $"{latest.PreviousClose.Value:F4}"
                    : "[grey]n/a[/]";

                table.AddRow(
                    $"[bold cyan]{series.Period}[/]",
                    latest?.RunningVwap is not null ? $"{latest.RunningVwap.Value:F4}" : "[grey]n/a[/]",
                    prevText,
                    $"{levels.Count} ({swept} swept)"
                );
            }

            AnsiConsole.Write(table);
        }

        var grouped = result.Classifications
            .GroupBy(item => item.VolumeClass, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        int Count(string key) => grouped.GetValueOrDefault(key);
        AnsiConsole.MarkupLine($"Volume classifications: [bold green]large {Count("large")}[/], [yellow]medium {Count("medium")}[/], [blue]low {Count("low")}[/], [grey]none {Count("none")}[/].");
    }

    public void RenderDatasets(IEnumerable<DatasetAliasDto> datasets)
    {
        var list = datasets.ToList();
        if (list.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No dataset aliases found.[/]");
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Alias");
        table.AddColumn("Market");
        table.AddColumn("Timeframe");
        table.AddColumn("From (UTC)");
        table.AddColumn("To (UTC)");

        foreach (var ds in list)
        {
            table.AddRow(
                $"[bold cyan]{ds.Alias}[/]",
                ds.MarketId,
                $"[yellow]{ds.Timeframe}[/]",
                ds.FromUtc.ToString("yyyy-MM-dd HH:mm"),
                ds.ToUtc.ToString("yyyy-MM-dd HH:mm")
            );
        }

        AnsiConsole.Write(table);
    }

    public void RenderRuns(IEnumerable<RunListingDto> runs)
    {
        var list = runs.ToList();
        if (list.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No backtest runs found.[/]");
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Run ID");
        table.AddColumn("Market");
        table.AddColumn("Timeframe");
        table.AddColumn("Range");
        table.AddColumn("Candles");
        table.AddColumn("Written (UTC)");

        foreach (var r in list)
        {
            table.AddRow(
                $"[bold cyan]{r.RunId}[/]",
                r.Instrument?.Market ?? "-",
                $"[yellow]{r.Instrument?.Timeframe ?? "-"}[/]",
                r.FromUtc.HasValue && r.ToUtc.HasValue ? $"{r.FromUtc.Value:yyyy-MM-dd} -> {r.ToUtc.Value:yyyy-MM-dd}" : "-",
                r.CandleCount.HasValue ? $"{r.CandleCount.Value:N0}" : "-",
                r.LastWrittenAtUtc.ToString("yyyy-MM-dd HH:mm")
            );
        }

        AnsiConsole.Write(table);
    }

    public void RenderTrades(IEnumerable<TradeSummaryDto> trades)
    {
        var list = trades.ToList();
        if (list.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No trades found.[/]");
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Trade ID");
        table.AddColumn("Source");
        table.AddColumn("Market");
        table.AddColumn("Direction");
        table.AddColumn("Setup");
        table.AddColumn("Reported R");
        table.AddColumn("Net R");
        table.AddColumn("Status");

        foreach (var t in list)
        {
            var pnlColor = t.NetR.HasValue && t.NetR.Value >= 0 ? "green" : "red";
            var pnlText = t.NetR.HasValue ? $"[{pnlColor}]{t.NetR.Value:+0.00;-0.00;0.00}R[/]" : "[grey]-[/]";
            var dirColor = t.Direction.Equals("long", StringComparison.OrdinalIgnoreCase) ? "green" : "red";

            table.AddRow(
                $"[bold]{Markup.Escape(t.Id)}[/]",
                Markup.Escape(t.Source),
                $"[cyan]{Markup.Escape(t.Instrument.Market)}[/]",
                $"[{dirColor}]{Markup.Escape(t.Direction.ToUpperInvariant())}[/]",
                Markup.Escape(t.SetupName ?? "-"),
                t.ReportedR.HasValue ? $"{t.ReportedR.Value:F2}R" : "-",
                pnlText,
                Markup.Escape(t.DerivedStatus)
            );
        }

        AnsiConsole.Write(table);
    }

    public void RenderAccounts(IEnumerable<AccountDto> accounts)
    {
        var list = accounts.ToList();
        if (list.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No accounts found.[/]");
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Account ID");
        table.AddColumn("Name");
        table.AddColumn("Kind");
        table.AddColumn("Latest equity");
        table.AddColumn("Status");

        foreach (var a in list)
        {
            table.AddRow(
                $"[bold cyan]{a.Id}[/]",
                a.Name,
                a.Kind,
                $"{a.EquityObservations.LastOrDefault()?.Equity ?? 0m:N2} {a.Currency}",
                a.EquityObservations.Count > 0 ? "[green]Observed[/]" : "[grey]No equity[/]"
            );
        }

        AnsiConsole.Write(table);
    }

    public void RenderAccountRisk(AccountRiskDto risk)
    {
        var panel = new Panel(
            new Rows(
                new Markup($"[bold]Account ID:[/] [cyan]{risk.AccountId}[/]"),
                new Markup($"[bold]As of:[/] {risk.AsOfUtc:yyyy-MM-dd HH:mm:ss}Z"),
                new Markup($"[bold]Open risk:[/] [yellow]{risk.OpenRisk:N2}[/]"),
                new Markup($"[bold]Concurrent warnings:[/] {(risk.ConcurrentRiskWarnings.Count == 0 ? "[green]0[/]" : $"[red]{risk.ConcurrentRiskWarnings.Count}[/]")}")
            )
        )
        {
            Header = new PanelHeader("[bold cyan]Account Risk Assessment[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
    }
}
