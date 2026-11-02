using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Configuration;
using Wickd.Cli.Rendering;

namespace Wickd.Cli.Commands.Config;

public sealed class ConfigInitCommand : Command<ConfigInitCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [Description("Overwrite existing configuration file if present.")]
        [CommandOption("-f|--force")]
        public bool Force { get; init; }
    }

    private readonly IConfigManager _configManager;
    private readonly IConsoleRenderer _renderer;

    public ConfigInitCommand(IConfigManager configManager, IConsoleRenderer renderer)
    {
        _configManager = configManager;
        _renderer = renderer;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (created, path) = _configManager.InitConfig(settings.ConfigPath, settings.Force);
        if (created)
        {
            _renderer.RenderSuccess($"Created user configuration file at: [cyan]{path}[/]");
        }
        else
        {
            _renderer.RenderWarning($"Configuration file already exists at: [cyan]{path}[/]. Use --force to overwrite.");
        }

        return ExitCodes.Success;
    }
}

public sealed class ConfigPathCommand : Command<GlobalCommandSettings>
{
    private readonly IConfigManager _configManager;

    public ConfigPathCommand(IConfigManager configManager)
    {
        _configManager = configManager;
    }

    protected override int Execute(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        var path = _configManager.GetActiveConfigPath(settings.ConfigPath);
        AnsiConsole.WriteLine(path);
        return ExitCodes.Success;
    }
}

public sealed class ConfigGetCommand : Command<ConfigGetCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [Description("Configuration key name (e.g. apiUrl, defaultMarket).")]
        [CommandArgument(0, "[KEY]")]
        public string? Key { get; init; }
    }

    private readonly IConfigManager _configManager;
    private readonly IConsoleRenderer _renderer;

    public ConfigGetCommand(IConfigManager configManager, IConsoleRenderer renderer)
    {
        _configManager = configManager;
        _renderer = renderer;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = _configManager.LoadConfig(settings.ConfigPath);

        if (string.IsNullOrWhiteSpace(settings.Key))
        {
            config.ApiToken = string.IsNullOrWhiteSpace(config.ApiToken) ? null : "(configured)";
            config.SessionToken = string.IsNullOrWhiteSpace(config.SessionToken) ? null : "(configured)";
            _renderer.RenderJson(config);
            return ExitCodes.Success;
        }

        var key = settings.Key.ToLowerInvariant();
        switch (key)
        {
            case "apiurl":
            case "api-url":
                AnsiConsole.WriteLine(config.ApiUrl);
                break;
            case "apitoken":
            case "api-token":
            case "token":
                AnsiConsole.WriteLine(string.IsNullOrWhiteSpace(config.ApiToken) ? string.Empty : "(configured)");
                break;
            case "sessiontoken":
            case "session-token":
                AnsiConsole.WriteLine(string.IsNullOrWhiteSpace(config.SessionToken) ? string.Empty : "(configured)");
                break;
            case "workspaceid":
            case "workspace-id":
                AnsiConsole.WriteLine(config.WorkspaceId ?? string.Empty);
                break;
            case "defaultexchange":
            case "exchange":
                AnsiConsole.WriteLine(config.DefaultExchange);
                break;
            case "defaultmarket":
            case "market":
                AnsiConsole.WriteLine(config.DefaultMarket);
                break;
            case "defaulttimeframe":
            case "timeframe":
                AnsiConsole.WriteLine(config.DefaultTimeframe);
                break;
            default:
                _renderer.RenderError($"Unknown configuration key: {settings.Key}");
                return ExitCodes.ValidationError;
        }

        return ExitCodes.Success;
    }
}

public sealed class ConfigSetCommand : Command<ConfigSetCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [Description("Configuration key name to update.")]
        [CommandArgument(0, "<KEY>")]
        public string Key { get; init; } = string.Empty;

        [Description("New value to set.")]
        [CommandArgument(1, "<VALUE>")]
        public string Value { get; init; } = string.Empty;
    }

    private readonly IConfigManager _configManager;
    private readonly IConsoleRenderer _renderer;

    public ConfigSetCommand(IConfigManager configManager, IConsoleRenderer renderer)
    {
        _configManager = configManager;
        _renderer = renderer;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = _configManager.LoadConfig(settings.ConfigPath);
        var key = settings.Key.ToLowerInvariant();

        switch (key)
        {
            case "apiurl":
            case "api-url":
                config.ApiUrl = settings.Value;
                break;
            case "apitoken":
            case "api-token":
            case "token":
                config.ApiToken = string.IsNullOrWhiteSpace(settings.Value) ? null : settings.Value;
                break;
            case "sessiontoken":
            case "session-token":
                if (!string.IsNullOrWhiteSpace(settings.Value) &&
                    (settings.Value.Trim().Length > 2048 || settings.Value.Any(char.IsControl)))
                {
                    _renderer.RenderError("Session token is invalid.");
                    return ExitCodes.ValidationError;
                }
                config.SessionToken = string.IsNullOrWhiteSpace(settings.Value) ? null : settings.Value.Trim();
                break;
            case "workspaceid":
            case "workspace-id":
                if (!string.IsNullOrWhiteSpace(settings.Value) &&
                    (settings.Value.Trim().Length > 128 || settings.Value.Any(char.IsControl)))
                {
                    _renderer.RenderError("Workspace ID is invalid.");
                    return ExitCodes.ValidationError;
                }
                config.WorkspaceId = string.IsNullOrWhiteSpace(settings.Value) ? null : settings.Value.Trim();
                break;
            case "defaultexchange":
            case "exchange":
                config.DefaultExchange = settings.Value;
                break;
            case "defaultmarket":
            case "market":
                config.DefaultMarket = settings.Value;
                break;
            case "defaulttimeframe":
            case "timeframe":
                config.DefaultTimeframe = settings.Value;
                break;
            default:
                _renderer.RenderError($"Unsupported configuration key: {settings.Key}");
                return ExitCodes.ValidationError;
        }

        _configManager.SaveConfig(config, settings.ConfigPath);
        var displayedValue = key is "apitoken" or "api-token" or "token" or "sessiontoken" or "session-token"
            ? (string.IsNullOrWhiteSpace(settings.Value) ? "(cleared)" : "(configured)")
            : settings.Value;
        _renderer.RenderSuccess($"Updated [cyan]{settings.Key}[/] = [green]{displayedValue}[/]");
        return ExitCodes.Success;
    }
}
