using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Commands.Journal;
using Wickd.Cli.Common;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.Strategies;

public class StrategyExampleListSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<STRATEGY_ID>")]
    public string StrategyId { get; init; } = string.Empty;
    [CommandOption("--cursor <CURSOR>")]
    public string? Cursor { get; init; }
    [CommandOption("--category <CATEGORY>")]
    [Description("decisionOnly (default) or explicit outcomeExample references.")]
    public string Category { get; init; } = "decisionOnly";
    [CommandOption("--limit <COUNT>")]
    [DefaultValue(50)]
    public int Limit { get; init; } = 50;
}

public class StrategyExampleReadSettings : GlobalCommandSettings
{
    [CommandArgument(0, "<STRATEGY_ID>")]
    public string StrategyId { get; init; } = string.Empty;
    [CommandArgument(1, "<EXAMPLE_ID>")]
    public string ExampleId { get; init; } = string.Empty;
    [CommandOption("--cursor <CURSOR>")]
    public string? Cursor { get; init; }
    [CommandOption("--category <CATEGORY>")]
    [Description("decisionOnly (default) or explicit outcomeExample references. Evidence requires decisionOnly.")]
    public string Category { get; init; } = "decisionOnly";
}

public sealed class StrategyExampleCreateSettings : WriteSettings
{
    [CommandArgument(0, "<STRATEGY_ID>")]
    public string StrategyId { get; init; } = string.Empty;
}

public abstract class StrategyExampleCommand<TSettings>(IApiClientFactory factory) : AsyncCommand<TSettings>
    where TSettings : GlobalCommandSettings
{
    protected abstract V2Call BuildCall(TSettings settings, byte[]? request);

    protected override async Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            byte[]? request = null;
            if (settings is StrategyExampleCreateSettings write)
            {
                if (string.IsNullOrWhiteSpace(write.RequestPath)) throw new ArgumentException("--request is required.");
                var file = new FileInfo(write.RequestPath);
                if (!file.Exists || file.Length is 0 or > 65_536)
                    throw new ArgumentException("Strategy-example request files must contain between 1 byte and 64 KiB.");
                request = await File.ReadAllBytesAsync(write.RequestPath, cancellationToken);
                if (request.Length is 0 or > 65_536)
                    throw new ArgumentException("Strategy-example request files must contain between 1 byte and 64 KiB.");
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

public sealed class StrategyExampleCreateCommand(IApiClientFactory factory) : StrategyExampleCommand<StrategyExampleCreateSettings>(factory)
{
    protected override V2Call BuildCall(StrategyExampleCreateSettings s, byte[]? request) =>
        new(V2Operation.StrategyExampleCreate, Id: s.StrategyId, Request: request, IdempotencyKey: s.IdempotencyKey);
}
public sealed class StrategyExampleListCommand(IApiClientFactory factory) : StrategyExampleCommand<StrategyExampleListSettings>(factory)
{
    protected override V2Call BuildCall(StrategyExampleListSettings s, byte[]? request) =>
        new(V2Operation.StrategyExampleList, Id: s.StrategyId, Cursor: s.Cursor, Limit: s.Limit, ExampleCategory: s.Category);
}
public sealed class StrategyExampleGetCommand(IApiClientFactory factory) : StrategyExampleCommand<StrategyExampleReadSettings>(factory)
{
    protected override V2Call BuildCall(StrategyExampleReadSettings s, byte[]? request) =>
        new(V2Operation.StrategyExampleGet, Id: s.StrategyId, SubId: s.ExampleId, Cursor: s.Cursor, ExampleCategory: s.Category);
}
public sealed class StrategyExampleEvidenceCommand(IApiClientFactory factory) : StrategyExampleCommand<StrategyExampleReadSettings>(factory)
{
    protected override V2Call BuildCall(StrategyExampleReadSettings s, byte[]? request) =>
        new(V2Operation.StrategyExampleEvidence, Id: s.StrategyId, SubId: s.ExampleId, Cursor: s.Cursor, ExampleCategory: s.Category);
}
