using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Common;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Tests;

public sealed class SetupLifecycleClientTests
{
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Path { get; private set; }
        public byte[]? Request { get; private set; }
        public string? Key { get; private set; }
        public string? Match { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            Path = request.RequestUri!.PathAndQuery;
            Request = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token);
            Key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
            Match = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"setup-4\"");
            return response;
        }
    }

    private static WickdApiClient Client(Handler handler, string? session = "redacted-session", string? workspace = "workspace-synthetic") =>
        new(new HttpClient(handler), new WickdCliConfig { ApiUrl = "https://example.invalid", SessionToken = session, WorkspaceId = workspace });

    private static string Sample(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "contracts", "samples", name + ".v2.sample.json"));
    }

    private static byte[] Request(V2Operation operation)
    {
        var note = operation is V2Operation.SetupNoteAppend or V2Operation.SetupNoteRevise;
        var body = JsonNode.Parse(Sample(note ? "setup-note-request" : "setup-disposition-request"))!;
        if (note) body["supersedesId"] = operation == V2Operation.SetupNoteRevise ? "note/previous" : null;
        else body["command"] = operation switch { V2Operation.SetupSkip => "markSkipped", V2Operation.SetupMissed => "markMissed", _ => "cancel" };
        return Encoding.UTF8.GetBytes(" \n" + body.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    public static TheoryData<V2Operation, string[]> Operations => new()
    {
        { V2Operation.SetupSkip, ["setup", "skip"] },
        { V2Operation.SetupMissed, ["setup", "missed"] },
        { V2Operation.SetupCancel, ["setup", "cancel"] },
        { V2Operation.SetupNoteAppend, ["setup", "notes", "append"] },
        { V2Operation.SetupNoteRevise, ["setup", "notes", "revise"] }
    };

    [Theory, MemberData(nameof(Operations))]
    public async Task LifecycleCommandsSendOriginalBytesAndConcurrencyMetadata(V2Operation operation, string[] command)
    {
        var file = Path.GetTempFileName();
        try
        {
            var bytes = Request(operation);
            await File.WriteAllBytesAsync(file, bytes);
            var handler = new Handler(Sample("journal-setup"));
            var args = command.Concat(["setup/id", "--request", file, "--idempotency-key", "logical-write", "--if-match", "\"setup-3\""]).ToArray();
            var first = await Run(handler, args);
            var retry = await Run(handler, args);
            first.Exit.Should().Be(0);
            retry.Exit.Should().Be(0);
            handler.Count.Should().Be(2);
            handler.Request.Should().Equal(bytes);
            handler.Key.Should().Be("logical-write");
            handler.Match.Should().Be("\"setup-3\"");
            handler.Path.Should().Be("/api/v2/setups/setup%2Fid/" +
                (operation is V2Operation.SetupNoteAppend or V2Operation.SetupNoteRevise ? "notes" : "dispositions"));
            using var output = JsonDocument.Parse(first.Output);
            output.RootElement.GetProperty("eTag").GetString().Should().Be("\"setup-4\"");
            output.RootElement.GetProperty("body").GetProperty("contract").GetString().Should().Be("journal-setup");
        }
        finally { File.Delete(file); }
    }

    [Theory, MemberData(nameof(Operations))]
    public async Task LifecycleWritesRequireEtagAndCredentials(V2Operation operation, string[] _)
    {
        var handler = new Handler(Sample("journal-setup"));
        var call = new V2Call(operation, Id: "setup", Request: Request(operation), IdempotencyKey: "write");
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(call)).Should().ThrowAsync<ArgumentException>();
        foreach (var etag in new[] { "*", "W/\"setup-3\"", "setup-3", "\"a\", \"b\"" })
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(call with { IfMatch = etag })).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Client(handler, workspace: null).SendV2Async(call with { IfMatch = "\"setup-3\"" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(handler, session: null).SendV2Async(call with { IfMatch = "\"setup-3\"" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(call with { IfMatch = "\"setup-3\"", IdempotencyKey = null }))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(V2Operation.SetupSkip, "command", "cancel")]
    [InlineData(V2Operation.SetupMissed, "command", "markSkipped")]
    [InlineData(V2Operation.SetupCancel, "command", "markMissed")]
    [InlineData(V2Operation.SetupNoteAppend, "supersedesId", "existing-note")]
    [InlineData(V2Operation.SetupNoteRevise, "supersedesId", null)]
    public async Task CommandsRejectIntentMismatchWithoutRewritingRequest(V2Operation operation, string property, string? value)
    {
        var body = JsonNode.Parse(Request(operation))!;
        body[property] = value;
        var handler = new Handler(Sample("journal-setup"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(operation, Id: "setup",
            Request: Encoding.UTF8.GetBytes(body.ToJsonString()), IdempotencyKey: "key", IfMatch: "\"setup-3\"")))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task ListSnapshotCursorCanBePassedUnchangedToFrozenDetailCommand()
    {
        var cursor = new string('a', 300) + ".signed+/=&?";
        var list = JsonNode.Parse(Sample("journal-setup-list"))!;
        list["snapshotCursor"] = cursor;
        list["nextCursor"] = null;
        var result = await Run(new Handler(list.ToJsonString()), ["setup", "list"]);
        result.Exit.Should().Be(0);
        using var page = JsonDocument.Parse(result.Output);
        var snapshot = page.RootElement.GetProperty("body").GetProperty("snapshotCursor").GetString()!;
        snapshot.Should().Be(cursor);
        var handler = new Handler(Sample("journal-setup"));
        var detail = await Run(handler, ["setup", "get", "setup/id", "--cursor", snapshot]);
        detail.Exit.Should().Be(0);
        handler.Path.Should().Be("/api/v2/setups/setup%2Fid?cursor=" + Uri.EscapeDataString(cursor));
        using var output = JsonDocument.Parse(detail.Output);
        output.RootElement.GetProperty("body").GetProperty("publicationWatermark").GetInt64().Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("")] [InlineData(" x")] [InlineData("x\r\nInjected: value")]
    public async Task FrozenDetailRefusesInvalidCursorBeforeNetwork(string cursor)
    {
        var handler = new Handler(Sample("journal-setup"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.SetupGet, Id: "setup", Cursor: cursor)))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task RequiredSnapshotCursorAndOptionalLegacySupersedesRemainCanonical()
    {
        var list = JsonNode.Parse(Sample("journal-setup-list"))!;
        list.AsObject().Remove("snapshotCursor");
        await FluentActions.Awaiting(() => Client(new Handler(list.ToJsonString())).SendV2Async(new V2Call(V2Operation.SetupList)))
            .Should().ThrowAsync<InvalidOperationException>();
        var legacy = JsonNode.Parse(Sample("journal-setup"))!;
        var notes = legacy["data"]!["notes"]!.AsArray();
        notes.Should().NotBeEmpty();
        foreach (var note in notes) note!.AsObject().Remove("supersedesId");
        var detail = await Client(new Handler(legacy.ToJsonString())).SendV2Async(new V2Call(V2Operation.SetupGet, Id: "setup"));
        detail.Body.GetProperty("data").GetProperty("notes").GetArrayLength().Should().Be(notes.Count);
    }

    [Theory]
    [InlineData(404)] [InlineData(409)] [InlineData(412)] [InlineData(422)] [InlineData(428)]
    public async Task LifecycleApiRefusalsPreserveExactStatus(int status)
    {
        var handler = new Handler("{\"code\":\"lifecycle_refused\"}", (HttpStatusCode)status);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.SetupSkip,
            Id: "setup", Request: Request(V2Operation.SetupSkip), IdempotencyKey: "key", IfMatch: "\"setup-3\"")))
            .Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be((HttpStatusCode)status);
        error.Which.Message.Should().Contain("lifecycle_refused");
    }

    [Fact]
    public async Task HelpExplainsFrozenDetailAndNoteRevision()
    {
        var detail = await Run(new Handler("{}"), ["setup", "get", "--help"]);
        detail.Exit.Should().Be(0);
        detail.Output.Should().Contain("--cursor").And.Contain("snapshotCursor");
        var revise = await Run(new Handler("{}"), ["setup", "notes", "revise", "--help"]);
        revise.Exit.Should().Be(0);
        revise.Output.Should().Contain("--request").And.Contain("--if-match").And.Contain("--idempotency-key");
    }

    private sealed class Factory(Handler handler) : IApiClientFactory
    {
        public IWickdApiClient CreateClient(GlobalCommandSettings settings) => Client(handler);
        public WickdCliConfig GetEffectiveConfig(GlobalCommandSettings settings) => new();
    }

    private static async Task<(int Exit, string Output)> Run(Handler handler, string[] args)
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No, Out = new AnsiConsoleOutput(output) });
        console.Profile.Width = 80;
        var services = new ServiceCollection();
        services.AddSingleton<IApiClientFactory>(new Factory(handler));
        var prior = AnsiConsole.Console;
        try
        {
            AnsiConsole.Console = console;
            var app = Program.CreateCommandApp(new TypeRegistrar(services), console);
            return (await app.RunAsync(args), output.ToString());
        }
        finally { AnsiConsole.Console = prior; }
    }
}
