using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Manage;

public class ManageDatasetsListCommand : AsyncCommand<GlobalCommandSettings>
{
    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public ManageDatasetsListCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        var client = _apiClientFactory.CreateClient(settings);
        List<DatasetAliasDto>? aliases = null;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Retrieving saved datasets...", async _ =>
                {
                    aliases = await client.GetDatasetAliasesAsync(cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError("Failed to list dataset aliases.", ex);
            return ExitCodes.Error;
        }

        if (aliases == null)
        {
            _renderer.RenderError("No response from server.");
            return ExitCodes.Error;
        }

        if (settings.Json)
        {
            _renderer.RenderJson(aliases);
        }
        else
        {
            _renderer.RenderDatasets(aliases);
        }

        return ExitCodes.Success;
    }
}

public class ManageDatasetsDeleteCommand : AsyncCommand<ManageDatasetsDeleteCommand.Settings>
{
    public class Settings : GlobalCommandSettings
    {
        [Description("Dataset alias name to delete.")]
        [CommandOption("-a|--alias <NAME>")]
        public string? Alias { get; init; }

        [Description("Also delete underlying candle cache if unreferenced.")]
        [CommandOption("--delete-cache")]
        public bool DeleteCache { get; init; }
    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public ManageDatasetsDeleteCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Alias))
        {
            _renderer.RenderError("--alias <name> is required.");
            return ExitCodes.ValidationError;
        }

        var client = _apiClientFactory.CreateClient(settings);
        var success = false;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting dataset alias '{settings.Alias}'...", async _ =>
                {
                    success = await client.DeleteDatasetAliasAsync(settings.Alias, settings.DeleteCache, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError($"Failed to delete dataset alias '{settings.Alias}'.", ex);
            return ExitCodes.Error;
        }

        if (success)
        {
            _renderer.RenderSuccess($"Dataset alias [cyan]{settings.Alias}[/] deleted successfully.");
            return ExitCodes.Success;
        }
        else
        {
            _renderer.RenderError($"Dataset alias '{settings.Alias}' was not found or could not be deleted.");
            return ExitCodes.Error;
        }
    }
}

public class ManageRunsListCommand : AsyncCommand<GlobalCommandSettings>
{
    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public ManageRunsListCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        var client = _apiClientFactory.CreateClient(settings);
        List<RunListingDto>? runs = null;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Retrieving backtest runs...", async _ =>
                {
                    runs = await client.GetRunsAsync(cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError("Failed to list backtest runs.", ex);
            return ExitCodes.Error;
        }

        if (runs == null)
        {
            _renderer.RenderError("No response from server.");
            return ExitCodes.Error;
        }

        if (settings.Json)
        {
            _renderer.RenderJson(runs);
        }
        else
        {
            _renderer.RenderRuns(runs);
        }

        return ExitCodes.Success;
    }
}

public class ManageRunsGetCommand : AsyncCommand<ManageRunsGetCommand.Settings>
{
    public class Settings : GlobalCommandSettings
    {
        [Description("Run ID to inspect.")]
        [CommandOption("-r|--run-id <ID>")]
        public string? RunId { get; init; }
    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public ManageRunsGetCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.RunId))
        {
            _renderer.RenderError("--run-id <id> is required.");
            return ExitCodes.ValidationError;
        }

        var client = _apiClientFactory.CreateClient(settings);
        InspectionRunDto? run = null;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching details for run '{settings.RunId}'...", async _ =>
                {
                    run = await client.GetRunAsync(settings.RunId, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError($"Failed to fetch run '{settings.RunId}'.", ex);
            return ExitCodes.Error;
        }

        if (run == null)
        {
            _renderer.RenderError($"Run '{settings.RunId}' was not found.");
            return ExitCodes.Error;
        }

        _renderer.RenderJson(run);
        return ExitCodes.Success;
    }
}

public class ManageRunsDeleteCommand : AsyncCommand<ManageRunsDeleteCommand.Settings>
{
    public class Settings : GlobalCommandSettings
    {
        [Description("Run ID to delete.")]
        [CommandOption("-r|--run-id <ID>")]
        public string? RunId { get; init; }

        [Description("Force deletion of run output.")]
        [CommandOption("-f|--force")]
        public bool Force { get; init; }
    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public ManageRunsDeleteCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.RunId))
        {
            _renderer.RenderError("--run-id <id> is required.");
            return ExitCodes.ValidationError;
        }

        if (!settings.Force)
        {
            _renderer.RenderError("Deleting a run requires --force.");
            return ExitCodes.ValidationError;
        }

        var client = _apiClientFactory.CreateClient(settings);
        var success = false;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting run '{settings.RunId}'...", async _ =>
                {
                    success = await client.DeleteRunAsync(settings.RunId, settings.Force, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError($"Failed to delete run '{settings.RunId}'.", ex);
            return ExitCodes.Error;
        }

        if (success)
        {
            _renderer.RenderSuccess($"Run [cyan]{settings.RunId}[/] deleted successfully.");
            return ExitCodes.Success;
        }
        else
        {
            _renderer.RenderError($"Run '{settings.RunId}' was not found or could not be deleted.");
            return ExitCodes.Error;
        }
    }
}
