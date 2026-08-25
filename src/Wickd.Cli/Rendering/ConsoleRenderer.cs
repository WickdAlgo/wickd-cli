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
                new Markup($"[bold]Candles Fetched:[/] [green]{result.CandlesFetched:N0}[/]"),
                new Markup($"[bold]Gaps Detected:[/] {(result.Gaps > 0 ? $"[yellow]{result.Gaps}[/]" : "[green]0[/]")}"),
                new Markup($"[bold]Alias:[/] {(string.IsNullOrEmpty(result.Alias) ? "[grey]none[/]" : $"[cyan]{result.Alias}[/]")}")
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
                new Markup($"[bold]Candles Replayed:[/] [green]{result.CandlesCount:N0}[/]"),
                new Markup($"[bold]Structure Events:[/] [yellow]{result.StructureEventsCount:N0}[/]")
            )
        )
        {
            Header = new PanelHeader("[bold green]Backtest Run Completed[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);

        if (result.Structures.Count > 0)
        {
            var table = new Table().RoundedBorder();
            table.AddColumn("Time (UTC)");
            table.AddColumn("Kind");
            table.AddColumn("Subject");
            table.AddColumn("Trigger");
            table.AddColumn("Price");

            foreach (var evt in result.Structures.Take(25))
            {
                table.AddRow(
                    evt.OpenTimeUtc?.ToString("yyyy-MM-dd HH:mm") ?? "-",
                    $"[cyan]{evt.Kind}[/]",
                    evt.Subject ?? "-",
                    evt.Trigger ?? "-",
                    evt.Price?.ToString("F4") ?? "-"
                );
            }

            if (result.Structures.Count > 25)
            {
                table.Caption = new TableTitle($"[grey]Showing 25 of {result.Structures.Count} structure events[/]");
            }

            AnsiConsole.Write(table);
        }
    }

    public void RenderVwapAnalysis(VwapAnalysisResultDto result)
    {
        AnsiConsole.MarkupLine($"Analyzed [bold green]{result.CandlesAnalyzed}[/] candles for [bold cyan]{result.MarketId}[/] [yellow]{result.Timeframe}[/] from {result.FromUtc:yyyy-MM-ddTHH:mm:ssZ} to {result.ToUtc:yyyy-MM-ddTHH:mm:ssZ} (gaps {result.Gaps}).");

        if (result.Summaries.Count > 0)
        {
            var table = new Table().RoundedBorder();
            table.AddColumn("Period");
            table.AddColumn("Now (Running VWAP)");
            table.AddColumn("Previous Close");
            table.AddColumn("Levels");

            foreach (var s in result.Summaries)
            {
                var prevText = s.PreviousClose.HasValue
                    ? $"{s.PreviousClose.Value:F4}{(s.IsSwept ? " [red](swept)[/]" : "")}"
                    : "[grey]n/a[/]";

                table.AddRow(
                    $"[bold cyan]{s.Period}[/]",
                    s.CurrentVwap.HasValue ? $"{s.CurrentVwap.Value:F4}" : "[grey]n/a[/]",
                    prevText,
                    $"{s.LevelsCount} ({s.SweptLevelsCount} swept, {s.ExpiredLevelsCount} expired)"
                );
            }

            AnsiConsole.Write(table);
        }

        AnsiConsole.MarkupLine($"Volume classifications: [bold green]large {result.VolumeSummary.Large}[/], [yellow]medium {result.VolumeSummary.Medium}[/], [blue]low {result.VolumeSummary.Low}[/], [grey]none {result.VolumeSummary.None}[/].");
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
        table.AddColumn("Candles");

        foreach (var ds in list)
        {
            table.AddRow(
                $"[bold cyan]{ds.Alias}[/]",
                ds.MarketId,
                $"[yellow]{ds.Timeframe}[/]",
                ds.FromUtc.ToString("yyyy-MM-dd HH:mm"),
                ds.ToUtc.ToString("yyyy-MM-dd HH:mm"),
                $"{ds.CandlesCount:N0}"
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
        table.AddColumn("Structures");
        table.AddColumn("Created (UTC)");

        foreach (var r in list)
        {
            table.AddRow(
                $"[bold cyan]{r.RunId}[/]",
                r.MarketId ?? "-",
                $"[yellow]{r.Timeframe ?? "-"}[/]",
                r.FromUtc.HasValue && r.ToUtc.HasValue ? $"{r.FromUtc.Value:yyyy-MM-dd} -> {r.ToUtc.Value:yyyy-MM-dd}" : "-",
                r.CandlesCount.HasValue ? $"{r.CandlesCount.Value:N0}" : "-",
                r.StructureEventsCount.HasValue ? $"{r.StructureEventsCount.Value:N0}" : "-",
                r.CreatedAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? "-"
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
        table.AddColumn("Account");
        table.AddColumn("Symbol");
        table.AddColumn("Direction");
        table.AddColumn("Entry");
        table.AddColumn("Exit");
        table.AddColumn("PnL");
        table.AddColumn("Status");

        foreach (var t in list)
        {
            var pnlColor = t.Pnl.HasValue && t.Pnl.Value >= 0 ? "green" : "red";
            var pnlText = t.Pnl.HasValue ? $"[{pnlColor}]{t.Pnl.Value:+#,##0.00;-#,##0.00;0.00}[/]" : "[grey]-[/]";
            var dirColor = t.Direction.Equals("long", StringComparison.OrdinalIgnoreCase) ? "green" : "red";

            table.AddRow(
                $"[bold]{t.TradeId}[/]",
                t.AccountId,
                $"[cyan]{t.Symbol}[/]",
                $"[{dirColor}]{t.Direction.ToUpperInvariant()}[/]",
                $"{t.EntryPrice:F4}",
                t.ExitPrice.HasValue ? $"{t.ExitPrice.Value:F4}" : "-",
                pnlText,
                t.Status
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
        table.AddColumn("Exchange");
        table.AddColumn("Balance");
        table.AddColumn("Status");

        foreach (var a in list)
        {
            table.AddRow(
                $"[bold cyan]{a.AccountId}[/]",
                a.Name,
                a.ExchangeId,
                $"{a.Balance:N2} {a.Currency}",
                a.IsActive ? "[green]Active[/]" : "[grey]Inactive[/]"
            );
        }

        AnsiConsole.Write(table);
    }

    public void RenderAccountRisk(AccountRiskDto risk)
    {
        var panel = new Panel(
            new Rows(
                new Markup($"[bold]Account ID:[/] [cyan]{risk.AccountId}[/]"),
                new Markup($"[bold]Total Equity:[/] [green]{risk.TotalEquity:N2} USDT[/]"),
                new Markup($"[bold]Utilized Margin:[/] [yellow]{risk.UtilizedMargin:N2} USDT[/]"),
                new Markup($"[bold]Free Margin:[/] [green]{risk.FreeMargin:N2} USDT[/]"),
                new Markup($"[bold]Open Positions:[/] {risk.OpenPositionsCount}"),
                new Markup($"[bold]Risk Score:[/] {(risk.RiskScore > 0.7 ? $"[red]{risk.RiskScore:P1}[/]" : $"[green]{risk.RiskScore:P1}[/]")}")
            )
        )
        {
            Header = new PanelHeader("[bold cyan]Account Risk Assessment[/]"),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);
    }
}
