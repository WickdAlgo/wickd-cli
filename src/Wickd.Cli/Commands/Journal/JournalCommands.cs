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

public sealed class SetupAttemptListSettings : PageSettings
{
    [CommandArgument(0, "<SETUP_ID>")]
    public string SetupId { get; init; } = string.Empty;
}

public sealed class SetupAttemptDetailSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<SETUP_ID>")]
    public string SetupId { get; init; } = string.Empty;

    [CommandArgument(1, "<ATTEMPT_ID>")]
    public string AttemptId { get; init; } = string.Empty;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque snapshotCursor from setup list or setup attempts list; reconstruct detail at that frozen publication.")]
    public string? Cursor { get; init; }
}

public sealed class PaperFillCreateSettings : WriteSettings
{
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
}

public sealed class PaperFillListSettings : PageSettings
{
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;
}

public sealed class PaperFillDetailSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;

    [CommandArgument(1, "<FILL_ID>")]
    public string FillId { get; init; } = string.Empty;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque snapshotCursor from this wallet's fills list; reads the fill at that frozen publication.")]
    public string? Cursor { get; init; }
}

public sealed class PaperFillCorrectionSettings : WriteSettings
{
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;

    [CommandArgument(1, "<FILL_ID>")]
    public string FillId { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Exact quoted strong ETag from this wallet's fills read or preceding write.")]
    public string IfMatch { get; init; } = string.Empty;
}

public class WalletInstrumentSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;

    [CommandArgument(1, "<INSTRUMENT_ID>")]
    public string InstrumentId { get; init; } = string.Empty;
}

public class WalletInstrumentPageSettings : PageSettings
{
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;

    [CommandArgument(1, "<INSTRUMENT_ID>")]
    public string InstrumentId { get; init; } = string.Empty;
}

public sealed class WalletAccountingListSettings : WalletInstrumentPageSettings;

public class WalletInstrumentWriteSettings : WriteSettings
{
    [CommandArgument(0, "<WALLET_ID>")]
    public string WalletId { get; init; } = string.Empty;

    [CommandArgument(1, "<INSTRUMENT_ID>")]
    public string InstrumentId { get; init; } = string.Empty;
}

public sealed class PaperHistoryCreateSettings : WalletInstrumentWriteSettings
{
    [CommandOption("--if-match <ETAG>")]
    [Description("Current strong ETag from this wallet/instrument history list or read.")]
    public string IfMatch { get; init; } = string.Empty;
}

public sealed class PaperHistoryGetSettings : WalletInstrumentSettings
{
    [CommandArgument(2, "<ATTESTATION_ID>")]
    public string AttestationId { get; init; } = string.Empty;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque snapshotCursor from this wallet/instrument history list.")]
    public string? Cursor { get; init; }
}

public sealed class PaperHistoryCorrectionSettings : WalletInstrumentWriteSettings
{
    [CommandArgument(2, "<ATTESTATION_ID>")]
    public string AttestationId { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Current strong ETag from this wallet/instrument history list or read.")]
    public string IfMatch { get; init; } = string.Empty;
}

public sealed class WalletAccountingCreateSettings : WalletInstrumentWriteSettings;

public sealed class WalletAccountingGetSettings : WalletInstrumentSettings
{
    [CommandArgument(2, "<RECEIPT_ID>")]
    public string ReceiptId { get; init; } = string.Empty;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque snapshotCursor from this wallet/instrument accounting list.")]
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

public sealed class SetupAttemptCreateSettings : WriteSettings
{
    [CommandArgument(0, "<SETUP_ID>")]
    public string SetupId { get; init; } = string.Empty;

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
                Id: settings switch
                {
                    AmendSettings amend => amend.Id,
                    SetupAttemptCreateSettings attempt => attempt.SetupId,
                    SetupAttemptListSettings attempt => attempt.SetupId,
                    SetupAttemptDetailSettings attempt => attempt.SetupId,
                    PaperFillCreateSettings fill => fill.WalletId,
                    PaperFillListSettings fill => fill.WalletId,
                    PaperFillDetailSettings fill => fill.WalletId,
                    PaperFillCorrectionSettings fill => fill.WalletId,
                    WalletInstrumentSettings scoped => scoped.WalletId,
                    WalletInstrumentPageSettings scoped => scoped.WalletId,
                    WalletInstrumentWriteSettings scoped => scoped.WalletId,
                    _ => (settings as IdentitySettings)?.Id
                },
                SubId: settings switch
                {
                    SetupAttemptDetailSettings attempt => attempt.AttemptId,
                    PaperFillDetailSettings fill => fill.FillId,
                    PaperFillCorrectionSettings fill => fill.FillId,
                    PaperHistoryGetSettings history => history.AttestationId,
                    PaperHistoryCorrectionSettings history => history.AttestationId,
                    WalletAccountingGetSettings accounting => accounting.ReceiptId,
                    _ => null
                },
                InstrumentId: settings switch
                {
                    WalletInstrumentSettings scoped => scoped.InstrumentId,
                    WalletInstrumentPageSettings scoped => scoped.InstrumentId,
                    WalletInstrumentWriteSettings scoped => scoped.InstrumentId,
                    _ => null
                },
                Request: request, IdempotencyKey: (settings as WriteSettings)?.IdempotencyKey,
                IfMatch: settings switch
                {
                    AmendSettings amend => amend.IfMatch,
                    SetupAttemptCreateSettings attempt => attempt.IfMatch,
                    PaperFillCorrectionSettings fill => fill.IfMatch,
                    PaperHistoryCreateSettings history => history.IfMatch,
                    PaperHistoryCorrectionSettings history => history.IfMatch,
                    _ => null
                }, Limit: (settings as PageSettings)?.Limit ?? 50,
                Cursor: settings switch
                {
                    SetupDetailSettings detail => detail.Cursor,
                    SetupAttemptDetailSettings attempt => attempt.Cursor,
                    PaperFillDetailSettings fill => fill.Cursor,
                    PaperHistoryGetSettings history => history.Cursor,
                    WalletAccountingGetSettings accounting => accounting.Cursor,
                    PageSettings page => page.Cursor,
                    _ => null
                }), cancellationToken);
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

public sealed class SetupAttemptCreateCommand(IApiClientFactory factory) : JournalCommand<SetupAttemptCreateSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupAttemptCreate;
}

public sealed class SetupAttemptListCommand(IApiClientFactory factory) : JournalCommand<SetupAttemptListSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupAttemptList;
}

public sealed class SetupAttemptGetCommand(IApiClientFactory factory) : JournalCommand<SetupAttemptDetailSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.SetupAttemptGet;
}

public sealed class PaperFillCreateCommand(IApiClientFactory factory) : JournalCommand<PaperFillCreateSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.PaperFillCreate;
}

public sealed class PaperFillListCommand(IApiClientFactory factory) : JournalCommand<PaperFillListSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.PaperFillList;
}

public sealed class PaperFillGetCommand(IApiClientFactory factory) : JournalCommand<PaperFillDetailSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.PaperFillGet;
}

public sealed class PaperFillCorrectCommand(IApiClientFactory factory) : JournalCommand<PaperFillCorrectionSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.PaperFillCorrect;
}

public sealed class PaperHistoryCreateCommand(IApiClientFactory factory) : JournalCommand<PaperHistoryCreateSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.PaperHistoryCreate;
}

public sealed class PaperHistoryListCommand(IApiClientFactory factory) : JournalCommand<WalletInstrumentPageSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.PaperHistoryList;
}

public sealed class PaperHistoryGetCommand(IApiClientFactory factory) : JournalCommand<PaperHistoryGetSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.PaperHistoryGet;
}

public sealed class PaperHistoryCorrectCommand(IApiClientFactory factory) : JournalCommand<PaperHistoryCorrectionSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.PaperHistoryCorrect;
}

public sealed class WalletAccountingCreateCommand(IApiClientFactory factory) : JournalCommand<WalletAccountingCreateSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.WalletAccountingCreate;
}

public sealed class WalletAccountingListCommand(IApiClientFactory factory) : JournalCommand<WalletAccountingListSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.WalletAccountingList;
}

public sealed class WalletAccountingGetCommand(IApiClientFactory factory) : JournalCommand<WalletAccountingGetSettings>(factory)
{
    protected override V2Operation Operation => V2Operation.WalletAccountingGet;
}
