using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Accounts;

public sealed class AccountsListCommand : AsyncCommand<GlobalCommandSettings>
{
    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public AccountsListCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        var client = _apiClientFactory.CreateClient(settings);
        AccountsPayloadDto? payload = null;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching connected accounts...", async _ =>
                {
                    payload = await client.GetAccountsAsync(cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError("Failed to retrieve accounts.", ex);
            return ExitCodes.Error;
        }

        if (payload == null)
        {
            _renderer.RenderError("No response from server.");
            return ExitCodes.Error;
        }

        if (settings.Json)
        {
            _renderer.RenderJson(payload);
        }
        else
        {
            _renderer.RenderAccounts(payload.Accounts);
        }

        return ExitCodes.Success;
    }
}

public sealed class AccountsRiskCommand : AsyncCommand<AccountsRiskCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [Description("Account ID to inspect.")]
        [CommandOption("-a|--account-id <ID>")]
        public string? AccountId { get; init; }
    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public AccountsRiskCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountId))
        {
            _renderer.RenderError("--account-id <id> is required.");
            return ExitCodes.ValidationError;
        }

        var client = _apiClientFactory.CreateClient(settings);
        AccountRiskDto? risk = null;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Evaluating account risk for '{settings.AccountId}'...", async _ =>
                {
                    risk = await client.GetAccountRiskAsync(settings.AccountId, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError($"Failed to evaluate risk for '{settings.AccountId}'.", ex);
            return ExitCodes.Error;
        }

        if (risk == null)
        {
            _renderer.RenderError($"Account '{settings.AccountId}' was not found.");
            return ExitCodes.Error;
        }

        if (settings.Json)
        {
            _renderer.RenderJson(risk);
        }
        else
        {
            _renderer.RenderAccountRisk(risk);
        }

        return ExitCodes.Success;
    }
}
