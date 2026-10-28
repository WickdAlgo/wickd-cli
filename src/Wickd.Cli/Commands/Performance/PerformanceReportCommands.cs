using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Journal;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Performance;

/// <summary>Complete query request file and explicit retry identity.</summary>
public sealed class PerformanceReportCreateSettings : WriteSettings { }

/// <summary>Exact retained report identity.</summary>
public sealed class PerformanceReportReadSettings : IdentitySettings { }

/// <summary>Reason request file, exact prior result, and optimistic concurrency receipt.</summary>
public sealed class PerformanceReportReviseSettings : WriteSettings
{
    /// <summary>Prior retained result identity.</summary>
    [CommandArgument(0, "<RESULT_ID>")]
    public string ResultId { get; init; } = string.Empty;
    /// <summary>Strong ETag from the report being revised.</summary>
    [CommandOption("--if-match <ETAG>")]
    public string? IfMatch { get; init; }
}

/// <summary>Forwards validated files without reserializing financial contracts.</summary>
public abstract class PerformanceReportCommand<TSettings>(IApiClientFactory factory) : AsyncCommand<TSettings>
    where TSettings : GlobalCommandSettings
{
    /// <summary>Builds the authenticated remote operation.</summary>
    protected abstract V2Call BuildCall(TSettings settings, byte[]? request);

    /// <summary>Reads original bytes and prints the server payload and HTTP receipt.</summary>
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

/// <summary>Retains a complete server-qualified setup query.</summary>
public sealed class PerformanceReportCreateCommand(IApiClientFactory factory) : PerformanceReportCommand<PerformanceReportCreateSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(PerformanceReportCreateSettings s, byte[]? request) =>
        new(V2Operation.PerformanceReportCreate, Request: request, IdempotencyKey: s.IdempotencyKey);
}
/// <summary>Reads one frozen result.</summary>
public sealed class PerformanceReportGetCommand(IApiClientFactory factory) : PerformanceReportCommand<PerformanceReportReadSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(PerformanceReportReadSettings s, byte[]? request) => new(V2Operation.PerformanceReportGet, Id: s.Id);
}
/// <summary>Retains a reasoned revision without replacing the original result.</summary>
public sealed class PerformanceReportReviseCommand(IApiClientFactory factory) : PerformanceReportCommand<PerformanceReportReviseSettings>(factory)
{
    /// <inheritdoc />
    protected override V2Call BuildCall(PerformanceReportReviseSettings s, byte[]? request) =>
        new(V2Operation.PerformanceReportRevise, Id: s.ResultId, Request: request, IdempotencyKey: s.IdempotencyKey, IfMatch: s.IfMatch);
}
