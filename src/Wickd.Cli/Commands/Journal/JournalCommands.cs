using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Journal;

public class PageSettings : GlobalCommandSettings
{
    [CommandOption("--limit <COUNT>")]
    [Description("Maximum page size (1–100); nextCursor is returned unchanged.")]
    [DefaultValue(50)]
    public int Limit { get; init; } = 50;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque nextCursor from the preceding page in this workspace.")]
    public string? Cursor { get; init; }
}

public class IdentitySettings : GlobalCommandSettings
{
    [CommandArgument(0, "<ID>")]
    public string Id { get; init; } = string.Empty;
}

public sealed class SetupDetailSettings : IdentitySettings
{
    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque snapshotCursor from setup list; reconstruct detail at that frozen publication.")]
    public string? Cursor { get; init; }
}

public class WriteSettings : GlobalCommandSettings
{
    [CommandOption("--request <PATH>")]
    [Description("UTF-8 JSON request file; keep the original file unchanged for retries.")]
    public string RequestPath { get; init; } = string.Empty;

    [CommandOption("--idempotency-key <KEY>")]
    [Description("Explicit stable key for this logical write; reuse with the same request on retry.")]
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class AmendSettings : WriteSettings
{
    [CommandArgument(0, "<ID>")]
    public string Id { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Exact quoted strong ETag from setup get or the preceding write.")]
    public string IfMatch { get; init; } = string.Empty;
}

public abstract class JournalCommand<TSettings>(IApiClientFactory factory) : AsyncCommand<TSettings>
    where TSettings : GlobalCommandSettings
{
    protected abstract V2Operation Operation { get; }

    protected override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? request = null;
            if (settings is WriteSettings write)
            {
                if (string.IsNullOrWhiteSpace(write.RequestPath)) throw new ArgumentException("--request is required.");
                if (new FileInfo(write.RequestPath).Length > 1_048_576) throw new ArgumentException("Request file exceeds 1 MiB.");
                request = await File.ReadAllBytesAsync(write.RequestPath, cancellationToken);
            }
            var response = await factory.CreateClient(settings).SendV2Async(new V2Call(Operation,
                Id: settings is AmendSettings amend ? amend.Id : (settings as IdentitySettings)?.Id,
                Request: request, IdempotencyKey: (settings as WriteSettings)?.IdempotencyKey,
                IfMatch: (settings as AmendSettings)?.IfMatch, Limit: (settings as PageSettings)?.Limit ?? 50,
                Cursor: (settings as PageSettings)?.Cursor ?? (settings as SetupDetailSettings)?.Cursor), cancellationToken);
            // Write machine-readable JSON directly; Spectre rendering wraps strings to terminal width.
            await AnsiConsole.Console.Profile.Out.Writer.WriteLineAsync(
                JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
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

public sealed class CatalogAssetsCommand(IApiClientFactory factory) : JournalCommand<GlobalCommandSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.CatalogAssets;
}

public sealed class CatalogInstrumentsCommand(IApiClientFactory factory) : JournalCommand<GlobalCommandSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.CatalogInstruments;
}

public sealed class CatalogSpecsCommand(IApiClientFactory factory) : JournalCommand<GlobalCommandSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.CatalogSpecs;
}

public sealed class CatalogTimeframesCommand(IApiClientFactory factory) : JournalCommand<GlobalCommandSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.CatalogTimeframes;
}

public sealed class WalletCreateCommand(IApiClientFactory factory) : JournalCommand<WriteSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.WalletCreate;
}

public sealed class WalletListCommand(IApiClientFactory factory) : JournalCommand<PageSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.WalletList;
}

public sealed class WalletGetCommand(IApiClientFactory factory) : JournalCommand<IdentitySettings>(factory)
{
    protected override V2Operation Operation => V2Operation.WalletGet;
}

public sealed class WalletBalanceCommand(IApiClientFactory factory) : JournalCommand<IdentitySettings>(factory)
{
    protected override V2Operation Operation => V2Operation.WalletBalance;
}

public sealed class RiskPreviewCommand(IApiClientFactory factory) : JournalCommand<WriteSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.RiskPreview;
}

public sealed class SetupAcceptCommand(IApiClientFactory factory) : JournalCommand<WriteSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupAccept;
}

public sealed class SetupListCommand(IApiClientFactory factory) : JournalCommand<PageSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupList;
}

public sealed class SetupGetCommand(IApiClientFactory factory) : JournalCommand<SetupDetailSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupGet;
}

public sealed class SetupAmendCommand(IApiClientFactory factory) : JournalCommand<AmendSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupAmend;
}

public sealed class SetupSkipCommand(IApiClientFactory factory) : JournalCommand<AmendSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupSkip;
}

public sealed class SetupMissedCommand(IApiClientFactory factory) : JournalCommand<AmendSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupMissed;
}

public sealed class SetupCancelCommand(IApiClientFactory factory) : JournalCommand<AmendSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupCancel;
}

public sealed class SetupNoteAppendCommand(IApiClientFactory factory) : JournalCommand<AmendSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupNoteAppend;
}

public sealed class SetupNoteReviseCommand(IApiClientFactory factory) : JournalCommand<AmendSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupNoteRevise;
}
