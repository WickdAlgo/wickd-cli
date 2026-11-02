using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Workspaces;

public sealed class WorkspaceListCommand : AsyncCommand<GlobalCommandSettings>
{
    private readonly IApiClientFactory _factory;

    public WorkspaceListCommand(IApiClientFactory factory) => _factory = factory;

    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _factory.CreateClient(settings).GetWorkspacesAsync(cancellationToken);
            if (settings.Json) AnsiConsole.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            else
            {
                var table = new Table().AddColumn("ID").AddColumn("Name").AddColumn("Role").AddColumn("Selected");
                foreach (var workspace in result.Workspaces)
                    table.AddRow(Markup.Escape(workspace.WorkspaceId), Markup.Escape(workspace.Name), Markup.Escape(workspace.Role),
                        workspace.WorkspaceId == _factory.GetEffectiveConfig(settings).WorkspaceId ? "yes" : "");
                AnsiConsole.Write(table);
            }
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to list workspaces: {Markup.Escape(ex.Message)}[/]");
            return ExitCodes.Error;
        }
    }
}

public sealed class WorkspaceUseCommand : AsyncCommand<WorkspaceUseCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [Description("Workspace identifier to select.")]
        [CommandArgument(0, "<ID>")]
        public string WorkspaceToUse { get; init; } = string.Empty;
    }

    private readonly IConfigManager _configManager;
    private readonly IApiClientFactory _factory;

    public WorkspaceUseCommand(IConfigManager configManager, IApiClientFactory factory)
    {
        _configManager = configManager;
        _factory = factory;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.WorkspaceToUse) || settings.WorkspaceToUse.Length > 128 || settings.WorkspaceToUse.Any(char.IsControl))
        {
            AnsiConsole.MarkupLine("[red]workspace use requires a non-empty workspace identifier.[/]");
            return ExitCodes.ValidationError;
        }

        WorkspaceDetailPayloadDto workspace;
        try
        {
            workspace = await _factory.CreateClient(settings).GetWorkspaceAsync(settings.WorkspaceToUse.Trim(), cancellationToken);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Could not verify workspace membership: {Markup.Escape(ex.Message)}[/]");
            return ExitCodes.Error;
        }

        if (!workspace.Workspace.WorkspaceId.Equals(settings.WorkspaceToUse.Trim(), StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine("[red]The API returned a different workspace than the requested ID.[/]");
            return ExitCodes.Error;
        }

        var config = _configManager.LoadConfig(settings.ConfigPath);
        config.WorkspaceId = workspace.Workspace.WorkspaceId;
        _configManager.SaveConfig(config, settings.ConfigPath);
        AnsiConsole.MarkupLine($"Selected workspace [cyan]{Markup.Escape(workspace.Workspace.Name)}[/] ({Markup.Escape(config.WorkspaceId)}) in local configuration.");
        return ExitCodes.Success;
    }
}
