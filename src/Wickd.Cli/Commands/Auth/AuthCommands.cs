using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Configuration;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Auth;

public sealed class AuthLoginCommand : AsyncCommand<AuthLoginCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [Description("WickdAlgo API Bearer Token.")]
        [CommandArgument(0, "[TOKEN]")]
        public string? TokenArgument { get; init; }
    }

    private readonly IConfigManager _configManager;
    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public AuthLoginCommand(IConfigManager configManager, IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _configManager = configManager;
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var token = settings.TokenArgument ?? settings.Token;
        if (string.IsNullOrWhiteSpace(token))
        {
            token = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter your [cyan]WickdAlgo API Bearer Token[/]:")
                    .PromptStyle("yellow")
                    .Secret());
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            _renderer.RenderError("Token cannot be empty.");
            return ExitCodes.ValidationError;
        }

        var config = _configManager.LoadConfig(settings.ConfigPath);
        config.ApiToken = token.Trim();

        var client = _apiClientFactory.CreateClient(settings);
        bool isOk = false;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Verifying token with WickdAlgo API...", async _ =>
            {
                try
                {
                    isOk = await client.HealthCheckAsync(cancellationToken);
                }
                catch
                {
                    isOk = false;
                }
            });

        _configManager.SaveConfig(config, settings.ConfigPath);
        _renderer.RenderSuccess("Authentication token saved successfully.");

        if (!isOk)
        {
            _renderer.RenderWarning("Could not reach API server to verify token. Please check API URL using 'wickd config get apiUrl'.");
        }

        return ExitCodes.Success;
    }
}

public sealed class AuthStatusCommand : AsyncCommand<GlobalCommandSettings>
{
    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public AuthStatusCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        var config = _apiClientFactory.GetEffectiveConfig(settings);
        var hasToken = !string.IsNullOrWhiteSpace(config.ApiToken);

        AnsiConsole.MarkupLine($"[bold]API Endpoint:[/] [cyan]{config.ApiUrl}[/]");
        AnsiConsole.MarkupLine($"[bold]Token Configured:[/] {(hasToken ? "[green]Yes[/]" : "[yellow]No (Anonymous)[/]")}");

        var client = _apiClientFactory.CreateClient(settings);
        var reachable = false;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Pinging WickdAlgo API...", async _ =>
            {
                reachable = await client.HealthCheckAsync(cancellationToken);
            });

        if (reachable)
        {
            _renderer.RenderSuccess("API server is online and reachable.");
            return ExitCodes.Success;
        }
        else
        {
            _renderer.RenderError($"Unable to reach API server at '{config.ApiUrl}'.");
            return ExitCodes.Error;
        }
    }
}
