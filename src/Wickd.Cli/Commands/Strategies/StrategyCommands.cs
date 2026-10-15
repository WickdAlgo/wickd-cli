using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Strategies;

public sealed class StrategyListCommand : AsyncCommand<GlobalCommandSettings>
{
    private readonly IApiClientFactory _factory;

    public StrategyListCommand(IApiClientFactory factory) => _factory = factory;

    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _factory.CreateClient(settings).GetStrategiesAsync(cancellationToken);
            if (settings.Json) AnsiConsole.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            else
            {
                var table = new Table().AddColumn("ID").AddColumn("Name").AddColumn("Status").AddColumn("Published versions");
                foreach (var strategy in result.Data.Items)
                    table.AddRow(Markup.Escape(strategy.StrategyId), Markup.Escape(strategy.Name),
                        Markup.Escape(strategy.Archived ? "archived" : strategy.ResearchStatus), strategy.PublishedVersionCount.ToString());
                AnsiConsole.Write(table);
            }
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to list strategies: {Markup.Escape(ex.Message)}[/]");
            return ExitCodes.Error;
        }
    }
}

public sealed class StrategyGetCommand : AsyncCommand<StrategyGetCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [Description("Strategy identifier to inspect.")]
        [CommandArgument(0, "<ID>")]
        public string StrategyId { get; init; } = string.Empty;
    }

    private readonly IApiClientFactory _factory;

    public StrategyGetCommand(IApiClientFactory factory) => _factory = factory;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.StrategyId))
        {
            AnsiConsole.MarkupLine("[red]A strategy ID is required.[/]");
            return ExitCodes.ValidationError;
        }
        try
        {
            var result = await _factory.CreateClient(settings).GetStrategyAsync(settings.StrategyId, cancellationToken);
            AnsiConsole.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to get strategy: {Markup.Escape(ex.Message)}[/]");
            return ExitCodes.Error;
        }
    }
}
