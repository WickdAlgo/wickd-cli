using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Journal;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.WalletCapital;

/// <summary>Original capital evidence file and explicit logical retry identity.</summary>
public sealed class WalletCapitalCreateSettings : WriteSettings
{
    /// <summary>The authenticated workspace's native paper wallet.</summary>
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
}

/// <summary>One server-owned immutable capital history selection.</summary>
public sealed class WalletCapitalListSettings : GlobalCommandSettings
{
    /// <summary>The authenticated workspace's native paper wallet.</summary>
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
    /// <summary>Maximum number of movements in one page.</summary>
    [CommandOption("--limit <COUNT>")]
    [DefaultValue(50)]
    public int Limit { get; init; } = 50;
    /// <summary>Opaque next cursor from this wallet's preceding page.</summary>
    [CommandOption("--cursor <CURSOR>")]
    public string? Cursor { get; init; }
}

/// <summary>One retained movement, optionally at the list's frozen snapshot.</summary>
public sealed class WalletCapitalGetSettings : GlobalCommandSettings
{
    /// <summary>The authenticated workspace's native paper wallet.</summary>
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
    /// <summary>The retained movement identity.</summary>
    [CommandArgument(1, "<MOVEMENT_ID>")]
    public string MovementId { get; init; } = string.Empty;
    /// <summary>Opaque snapshot cursor from this wallet's list.</summary>
    [CommandOption("--cursor <CURSOR>")]
    public string? Cursor { get; init; }
}

/// <summary>Forwards original request bytes and renders the API's exact receipt.</summary>
public abstract class WalletCapitalCommand<TSettings>(IApiClientFactory factory) : AsyncCommand<TSettings>
    where TSettings : GlobalCommandSettings
{
    /// <summary>Constructs the selected authenticated operation.</summary>
    protected abstract V2Call BuildCall(TSettings settings, byte[]? request);
    /// <summary>Executes the remote operation without deriving balances or trading results.</summary>
    protected override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? request = null;
            if (settings is WriteSettings write)
            {
                if (string.IsNullOrWhiteSpace(write.RequestPath)) throw new ArgumentException("--request is required.");
                var file = new FileInfo(write.RequestPath);
                if (!file.Exists || file.Length is 0 or > 65_536) throw new ArgumentException("Request file must contain between 1 byte and 64 KiB.");
                request = await File.ReadAllBytesAsync(write.RequestPath, cancellationToken);
                if (request.Length is 0 or > 65_536) throw new ArgumentException("Request file must contain between 1 byte and 64 KiB.");
            }
            var response = await factory.CreateClient(settings).SendV2Async(BuildCall(settings, request), cancellationToken);
            await AnsiConsole.Console.Profile.Out.Writer.WriteLineAsync(JsonSerializer.Serialize(response,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            return ExitCodes.Success;
        }
        catch (Exception error) when (error is ArgumentException or JsonException or IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[red]Invalid request: {Markup.Escape(error.Message)}[/]");
            return ExitCodes.ValidationError;
        }
        catch (Exception error)
        {
            AnsiConsole.MarkupLine($"[red]API request failed: {Markup.Escape(error.Message)}[/]");
            return ExitCodes.Error;
        }
    }
}

/// <summary>Records an explicit paper deposit or withdrawal from a canonical request file.</summary>
public sealed class WalletCapitalCreateCommand(IApiClientFactory factory) : WalletCapitalCommand<WalletCapitalCreateSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(WalletCapitalCreateSettings s, byte[]? request) =>
        new(V2Operation.WalletCapitalCreate, Id: s.WalletId, Request: request, IdempotencyKey: s.IdempotencyKey);
}
/// <summary>Reads one frozen page of immutable capital evidence.</summary>
public sealed class WalletCapitalListCommand(IApiClientFactory factory) : WalletCapitalCommand<WalletCapitalListSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(WalletCapitalListSettings s, byte[]? request) =>
        new(V2Operation.WalletCapitalList, Id: s.WalletId, Limit: s.Limit, Cursor: s.Cursor);
}
/// <summary>Reads one movement at an optional frozen history coordinate.</summary>
public sealed class WalletCapitalGetCommand(IApiClientFactory factory) : WalletCapitalCommand<WalletCapitalGetSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(WalletCapitalGetSettings s, byte[]? request) =>
        new(V2Operation.WalletCapitalGet, Id: s.WalletId, SubId: s.MovementId, Cursor: s.Cursor);
}
