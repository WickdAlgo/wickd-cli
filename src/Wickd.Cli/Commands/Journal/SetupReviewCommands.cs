using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Journal;

public class SetupReviewReadSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<SETUP_ID>")]
    public string SetupId { get; init; } = string.Empty;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque cursor from the same setup review stream.")]
    public string? Cursor { get; init; }
}

public sealed class SetupReviewHistorySettings : SetupReviewReadSettings
{
    [CommandOption("--limit <COUNT>")]
    [Description("Maximum page size (1–200).")]
    [DefaultValue(50)]
    public int Limit { get; init; } = 50;
}

public sealed class SetupReviewRevisionSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<SETUP_ID>")]
    public string SetupId { get; init; } = string.Empty;

    [CommandArgument(1, "<REVIEW_ID>")]
    public string ReviewId { get; init; } = string.Empty;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque cursor from the same setup review stream.")]
    public string? Cursor { get; init; }
}

public sealed class SetupReviewAppendSettings : WriteSettings
{
    [CommandArgument(0, "<SETUP_ID>")]
    public string SetupId { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Exact strong setup-review ETag from `setup review get` or the preceding append.")]
    public string IfMatch { get; init; } = string.Empty;
}

public abstract class SetupReviewCommand<TSettings>(IApiClientFactory factory) : AsyncCommand<TSettings>
    where TSettings : GlobalCommandSettings
{
    protected abstract V2Call BuildCall(TSettings settings, byte[]? request);

    protected override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? request = null;
            if (settings is SetupReviewAppendSettings write)
            {
                if (string.IsNullOrWhiteSpace(write.RequestPath)) throw new ArgumentException("--request is required.");
                var file = new FileInfo(write.RequestPath);
                if (!file.Exists || file.Length is 0 or > 65_536)
                    throw new ArgumentException("Setup-review request files must contain between 1 byte and 64 KiB.");
                request = await File.ReadAllBytesAsync(write.RequestPath, cancellationToken);
                if (request.Length is 0 or > 65_536)
                    throw new ArgumentException("Setup-review request files must contain between 1 byte and 64 KiB.");
            }

            var response = await factory.CreateClient(settings).SendV2Async(BuildCall(settings, request), cancellationToken);
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

public sealed class SetupReviewGetCommand(IApiClientFactory factory) : SetupReviewCommand<SetupReviewReadSettings>(factory)
{
    protected override V2Call BuildCall(SetupReviewReadSettings settings, byte[]? request) =>
        new(V2Operation.SetupReviewGet, Id: settings.SetupId, Cursor: settings.Cursor);
}

public sealed class SetupReviewAppendCommand(IApiClientFactory factory) : SetupReviewCommand<SetupReviewAppendSettings>(factory)
{
    protected override V2Call BuildCall(SetupReviewAppendSettings settings, byte[]? request) =>
        new(V2Operation.SetupReviewAppend, Id: settings.SetupId, Request: request,
            IdempotencyKey: settings.IdempotencyKey, IfMatch: settings.IfMatch);
}

public sealed class SetupReviewRevisionCommand(IApiClientFactory factory) : SetupReviewCommand<SetupReviewRevisionSettings>(factory)
{
    protected override V2Call BuildCall(SetupReviewRevisionSettings settings, byte[]? request) =>
        new(V2Operation.SetupReviewRevisionGet, Id: settings.SetupId, SubId: settings.ReviewId, Cursor: settings.Cursor);
}

public sealed class SetupReviewHistoryCommand(IApiClientFactory factory) : SetupReviewCommand<SetupReviewHistorySettings>(factory)
{
    protected override V2Call BuildCall(SetupReviewHistorySettings settings, byte[]? request) =>
        new(V2Operation.SetupReviewHistory, Id: settings.SetupId, Limit: settings.Limit, Cursor: settings.Cursor);
}
