using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Journal;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.StrategyContext;

public class StrategyContextPageSettings : GlobalCommandSettings
{
    [CommandOption("--limit <COUNT>")]
    [Description("Maximum page size (1–200); nextCursor is returned unchanged.")]
    [DefaultValue(50)]
    public int Limit { get; init; } = 50;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque nextCursor from the preceding page in this workspace.")]
    public string? Cursor { get; init; }
}

public class StrategyContextIdentitySettings : GlobalCommandSettings
{
    [CommandArgument(0, "<ID>")]
    public string Id { get; init; } = string.Empty;

    [CommandOption("--cursor <CURSOR>")]
    [Description("Opaque frozen cursor from the matching list or prior read.")]
    public string? Cursor { get; init; }
}

public sealed class StrategyContextRecordSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<ID>")]
    public string Id { get; init; } = string.Empty;
}

public class StrategyContextWriteSettings : WriteSettings;

public sealed class SessionDefinitionVersionWriteSettings : StrategyContextWriteSettings
{
    [CommandArgument(0, "<SESSION_ID>")]
    public string SessionId { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Current strong ETag returned by the latest version write for this session.")]
    public string IfMatch { get; init; } = string.Empty;
}

public sealed class MarketConditionRevisionWriteSettings : StrategyContextWriteSettings
{
    [CommandArgument(0, "<TAG_ID>")]
    public string TagId { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Current strong ETag returned by the latest revision write for this condition tag.")]
    public string IfMatch { get; init; } = string.Empty;
}

public sealed class StrategyScopeUpdateSettings : StrategyContextWriteSettings
{
    [CommandArgument(0, "<VERSION_ID>")]
    public string VersionId { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Current draft ETag from either strategy scope read.")]
    public string IfMatch { get; init; } = string.Empty;
}

public sealed class StrategyScopeListSettings : StrategyContextPageSettings
{
    [CommandArgument(0, "<STRATEGY_ID>")]
    public string StrategyId { get; init; } = string.Empty;
}

public sealed class StrategyContextScopeUpdateSettings : StrategyContextWriteSettings
{
    [CommandArgument(0, "<VERSION_ID>")]
    public string VersionId { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Current shared draft ETag from either instrument/timeframe or context scope read.")]
    public string IfMatch { get; init; } = string.Empty;
}

public sealed class SetupContextWriteSettings : StrategyContextWriteSettings
{
    [CommandArgument(0, "<SETUP_ID>")]
    public string SetupId { get; init; } = string.Empty;

    [CommandOption("--if-match <ETAG>")]
    [Description("Current independent context ETag from setup context get.")]
    public string IfMatch { get; init; } = string.Empty;
}

public sealed class SetupContextPageSettings : StrategyContextPageSettings
{
    [CommandArgument(0, "<SETUP_ID>")]
    public string SetupId { get; init; } = string.Empty;
}

public sealed class SetupContextViewSettings : StrategyContextIdentitySettings
{
    [CommandOption("--session-version <VERSION_ID>")]
    [Description("Filter to an exact session-definition version.")]
    public string? SessionVersionId { get; init; }

    [CommandOption("--condition-tag <TAG_ID>")]
    [Description("Filter to a stable market-condition tag identity.")]
    public string? ConditionTagId { get; init; }
}

public abstract class StrategyContextCommand<TSettings>(IApiClientFactory factory) : AsyncCommand<TSettings>
    where TSettings : GlobalCommandSettings
{
    protected abstract V2Call BuildCall(TSettings settings, byte[]? request);

    protected override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? bytes = null;
            if (settings is StrategyContextWriteSettings write)
            {
                if (string.IsNullOrWhiteSpace(write.RequestPath)) throw new ArgumentException("--request is required.");
                var file = new FileInfo(write.RequestPath);
                if (!file.Exists) throw new FileNotFoundException("The strategy-context request file was not found.", write.RequestPath);
                if (file.Length is 0 or > 65_536) throw new ArgumentException("Strategy-context request files must contain between 1 byte and 64 KiB.");
                bytes = await File.ReadAllBytesAsync(write.RequestPath, cancellationToken);
                if (bytes.Length is 0 or > 65_536) throw new ArgumentException("Strategy-context request files must contain between 1 byte and 64 KiB.");
            }

            var response = await factory.CreateClient(settings).SendV2Async(BuildCall(settings, bytes), cancellationToken);
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

public sealed class SessionDefinitionCreateCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextWriteSettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextWriteSettings settings, byte[]? request) => new(V2Operation.SessionDefinitionCreate, Request: request, IdempotencyKey: settings.IdempotencyKey);
}

public sealed class SessionDefinitionVersionCreateCommand(IApiClientFactory factory) : StrategyContextCommand<SessionDefinitionVersionWriteSettings>(factory)
{
    protected override V2Call BuildCall(SessionDefinitionVersionWriteSettings settings, byte[]? request) => new(V2Operation.SessionDefinitionVersionCreate, Id: settings.SessionId, Request: request, IdempotencyKey: settings.IdempotencyKey, IfMatch: settings.IfMatch);
}

public sealed class SessionDefinitionVersionListCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextPageSettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextPageSettings settings, byte[]? request) => new(V2Operation.SessionDefinitionVersionList, Limit: settings.Limit, Cursor: settings.Cursor);
}

public sealed class SessionDefinitionVersionGetCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextRecordSettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextRecordSettings settings, byte[]? request) => new(V2Operation.SessionDefinitionVersionGet, Id: settings.Id);
}

public sealed class MarketConditionCreateCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextWriteSettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextWriteSettings settings, byte[]? request) => new(V2Operation.MarketConditionCreate, Request: request, IdempotencyKey: settings.IdempotencyKey);
}

public sealed class MarketConditionRevisionCreateCommand(IApiClientFactory factory) : StrategyContextCommand<MarketConditionRevisionWriteSettings>(factory)
{
    protected override V2Call BuildCall(MarketConditionRevisionWriteSettings settings, byte[]? request) => new(V2Operation.MarketConditionRevisionCreate, Id: settings.TagId, Request: request, IdempotencyKey: settings.IdempotencyKey, IfMatch: settings.IfMatch);
}

public sealed class MarketConditionRevisionListCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextPageSettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextPageSettings settings, byte[]? request) => new(V2Operation.MarketConditionRevisionList, Limit: settings.Limit, Cursor: settings.Cursor);
}

public sealed class MarketConditionRevisionGetCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextRecordSettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextRecordSettings settings, byte[]? request) => new(V2Operation.MarketConditionRevisionGet, Id: settings.Id);
}

public sealed class StrategyScopeGetCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextIdentitySettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextIdentitySettings settings, byte[]? request) => new(V2Operation.StrategyScopeGet, Id: settings.Id, Cursor: settings.Cursor);
}

public sealed class StrategyScopeUpdateCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyScopeUpdateSettings>(factory)
{
    protected override V2Call BuildCall(StrategyScopeUpdateSettings settings, byte[]? request) => new(V2Operation.StrategyScopeUpdate, Id: settings.VersionId, Request: request, IdempotencyKey: settings.IdempotencyKey, IfMatch: settings.IfMatch);
}

public sealed class StrategyScopeListCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyScopeListSettings>(factory)
{
    protected override V2Call BuildCall(StrategyScopeListSettings settings, byte[]? request) => new(V2Operation.StrategyScopeList, Id: settings.StrategyId, Limit: settings.Limit, Cursor: settings.Cursor);
}

public sealed class StrategyContextScopeGetCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextIdentitySettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextIdentitySettings settings, byte[]? request) => new(V2Operation.StrategyContextScopeGet, Id: settings.Id, Cursor: settings.Cursor);
}

public sealed class StrategyContextScopeUpdateCommand(IApiClientFactory factory) : StrategyContextCommand<StrategyContextScopeUpdateSettings>(factory)
{
    protected override V2Call BuildCall(StrategyContextScopeUpdateSettings settings, byte[]? request) => new(V2Operation.StrategyContextScopeUpdate, Id: settings.VersionId, Request: request, IdempotencyKey: settings.IdempotencyKey, IfMatch: settings.IfMatch);
}

public sealed class SetupContextObservationCreateCommand(IApiClientFactory factory) : StrategyContextCommand<SetupContextWriteSettings>(factory)
{
    protected override V2Call BuildCall(SetupContextWriteSettings settings, byte[]? request) => new(V2Operation.SetupContextObservationCreate, Id: settings.SetupId, Request: request, IdempotencyKey: settings.IdempotencyKey, IfMatch: settings.IfMatch);
}

public sealed class SetupContextObservationListCommand(IApiClientFactory factory) : StrategyContextCommand<SetupContextPageSettings>(factory)
{
    protected override V2Call BuildCall(SetupContextPageSettings settings, byte[]? request) => new(V2Operation.SetupContextObservationList, Id: settings.SetupId, Limit: settings.Limit, Cursor: settings.Cursor);
}

public sealed class SetupContextGetCommand(IApiClientFactory factory) : StrategyContextCommand<SetupContextViewSettings>(factory)
{
    protected override V2Call BuildCall(SetupContextViewSettings settings, byte[]? request) => new(V2Operation.SetupContextGet, Id: settings.Id, Cursor: settings.Cursor, SessionDefinitionVersionId: settings.SessionVersionId, MarketConditionTagId: settings.ConditionTagId);
}
