using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Trades;

public sealed class TradesListCommand : AsyncCommand<GlobalCommandSettings>
{
    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public TradesListCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalCommandSettings settings, CancellationToken cancellationToken)
    {
        var client = _apiClientFactory.CreateClient(settings);
        List<TradeSummaryDto>? trades = null;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching trade journal...", async _ =>
                {
                    trades = await client.GetTradesAsync(cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError("Failed to retrieve trade journal.", ex);
            return ExitCodes.Error;
        }

        if (trades == null)
        {
            _renderer.RenderError("No response from server.");
            return ExitCodes.Error;
        }

        if (settings.Json)
        {
            _renderer.RenderJson(trades);
        }
        else
        {
            _renderer.RenderTrades(trades);
        }

        return ExitCodes.Success;
    }
}

public sealed class TradesGetCommand : AsyncCommand<TradesGetCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [Description("Trade ID to inspect.")]
        [CommandOption("-t|--trade-id <ID>")]
        public string? TradeId { get; init; }
    }

    private readonly IApiClientFactory _apiClientFactory;
    private readonly IConsoleRenderer _renderer;

    public TradesGetCommand(IApiClientFactory apiClientFactory, IConsoleRenderer renderer)
    {
        _apiClientFactory = apiClientFactory;
        _renderer = renderer;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.TradeId))
        {
            _renderer.RenderError("--trade-id <id> is required.");
            return ExitCodes.ValidationError;
        }

        var client = _apiClientFactory.CreateClient(settings);
        TradeDetailDto? trade = null;

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching trade '{settings.TradeId}'...", async _ =>
                {
                    trade = await client.GetTradeAsync(settings.TradeId, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _renderer.RenderError($"Failed to fetch trade '{settings.TradeId}'.", ex);
            return ExitCodes.Error;
        }

        if (trade == null)
        {
            _renderer.RenderError($"Trade '{settings.TradeId}' was not found.");
            return ExitCodes.Error;
        }

        _renderer.RenderJson(trade);
        return ExitCodes.Success;
    }
}
