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

public sealed class SetupAttemptClientTests
{
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Path { get; private set; }
        public byte[]? Request { get; private set; }
        public string? Key { get; private set; }
        public string? Match { get; private set; }
        public string? Session { get; private set; }
        public string? Workspace { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            Path = request.RequestUri!.PathAndQuery;
            Request = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token);
            Key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
            Match = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            Session = request.Headers.TryGetValues("X-Wickd-Session", out var sessions) ? sessions.Single() : null;
            Workspace = request.Headers.TryGetValues("X-Wickd-Workspace", out var workspaces) ? workspaces.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static WickdApiClient Client(Handler handler, string? session = "session-synthetic", string? workspace = "workspace-synthetic") =>
        new(new HttpClient(handler), new WickdCliConfig
        {
            ApiUrl = "https://example.invalid",
            SessionToken = session,
            WorkspaceId = workspace,
            ApiToken = "service-synthetic"
        });

    private static string Sample(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "contracts", "samples", name + ".v2.sample.json"));
    }

    [Fact]
    public async Task CreateSendsOriginalBytesStableKeyCurrentEtagAndSessionWorkspace()
    {
        var file = Path.GetTempFileName();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(" \n{\"acceptedPlanRevisionId\":\"plan/1\",\"mode\":\"paper\",\"decisionAtUtc\":\"2026-10-15T09:00:00Z\"}\n");
            await File.WriteAllBytesAsync(file, bytes);
            var response = JsonNode.Parse(Sample("setup-attempt"))!;
            response["data"]!["setupId"] = "setup/id";
            var handler = new Handler(response.ToJsonString());
            var args = new[] { "setup", "attempts", "create", "setup/id", "--request", file,
                "--idempotency-key", "attempt-write-1", "--if-match", "\"setup-revision-2\"" };
            var first = await Run(handler, args);
            var retry = await Run(handler, args);

            first.Exit.Should().Be(0, first.Output);
            retry.Exit.Should().Be(0, retry.Output);
            handler.Count.Should().Be(2);
            handler.Path.Should().Be("/api/v2/setups/setup%2Fid/attempts");
            handler.Request.Should().Equal(bytes);
            handler.Key.Should().Be("attempt-write-1");
            handler.Match.Should().Be("\"setup-revision-2\"");
            handler.Session.Should().Be("session-synthetic");
            handler.Workspace.Should().Be("workspace-synthetic");
            handler.Authorization.Should().Be("Bearer service-synthetic");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ListUsesSetupScopedPaginationAndReturnsSnapshotCursorUnchanged()
    {
        var cursor = new string('a', 280) + ".signed+/=&?";
        var page = JsonNode.Parse(Sample("setup-attempt-list"))!;
        page["items"]![0]!["setupId"] = "setup/id";
        page["snapshotCursor"] = cursor;
        page["nextCursor"] = "opaque-next-page+/=";
        var handler = new Handler(page.ToJsonString());
        var result = await Run(handler, ["setup", "attempts", "list", "setup/id", "--limit", "12", "--cursor", "next cursor+/="]);
        result.Exit.Should().Be(0, result.Output);
        handler.Path.Should().Be("/api/v2/setups/setup%2Fid/attempts?limit=12&cursor=next%20cursor%2B%2F%3D");
        using var output = JsonDocument.Parse(result.Output);
        output.RootElement.GetProperty("body").GetProperty("snapshotCursor").GetString().Should().Be(cursor);
        output.RootElement.GetProperty("body").GetProperty("nextCursor").GetString().Should().Be("opaque-next-page+/=");
    }

    [Fact]
    public async Task GetRequiresBothIdsAndForwardsFrozenCursor()
    {
        var cursor = "signed-attempt-cursor+/=&?";
        var response = JsonNode.Parse(Sample("setup-attempt"))!;
        response["data"]!["setupId"] = "setup/id";
        response["data"]!["attemptId"] = "attempt/id";
        var handler = new Handler(response.ToJsonString());
        var result = await Run(handler, ["setup", "attempts", "get", "setup/id", "attempt/id", "--cursor", cursor]);
        result.Exit.Should().Be(0, result.Output);
        handler.Path.Should().Be("/api/v2/setups/setup%2Fid/attempts/attempt%2Fid?cursor=" + Uri.EscapeDataString(cursor));
        using var output = JsonDocument.Parse(result.Output);
        output.RootElement.GetProperty("body").GetProperty("data").GetProperty("attemptId").GetString().Should().Be("attempt/id");
    }

    [Fact]
    public async Task CreateRequiresCredentialsStrongEtagAndStableKeyBeforeNetwork()
    {
        var request = Encoding.UTF8.GetBytes(Sample("setup-attempt-create-request"));
        var handler = new Handler(Sample("setup-attempt"));
        var call = new V2Call(V2Operation.SetupAttemptCreate, Id: "setup", Request: request,
            IdempotencyKey: "write-key", IfMatch: "\"setup-2\"");
        foreach (var invalid in new[] { "*", "W/\"setup-2\"", "setup-2", "\"a\", \"b\"" })
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(call with { IfMatch = invalid })).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Client(handler, workspace: null).SendV2Async(call)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(handler, session: null).SendV2Async(call)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(call with { IdempotencyKey = null })).Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("{\"acceptedPlanRevisionId\":\"plan\",\"mode\":\"live\",\"decisionAtUtc\":\"2026-10-15T09:00:00Z\"}")]
    [InlineData("{\"acceptedPlanRevisionId\":\"plan\",\"mode\":\"paper\",\"decisionAtUtc\":123}")]
    [InlineData("{\"acceptedPlanRevisionId\":\"plan\",\"mode\":\"paper\"}")]
    public async Task CreateRejectsUnsupportedOrMalformedRequestWithoutChangingIt(string json)
    {
        var handler = new Handler(Sample("setup-attempt"));
        var call = new V2Call(V2Operation.SetupAttemptCreate, Id: "setup", Request: Encoding.UTF8.GetBytes(json),
            IdempotencyKey: "write-key", IfMatch: "\"setup-2\"");
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(call)).Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task AttemptResponsesMustMatchNestedSetupAndAttemptIds()
    {
        var wrongSetup = JsonNode.Parse(Sample("setup-attempt"))!;
        wrongSetup["data"]!["setupId"] = "other-setup";
        await FluentActions.Awaiting(() => Client(new Handler(wrongSetup.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.SetupAttemptGet, Id: "setup", SubId: "attempt-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*setup and attempt identities*");

        var wrongAttempt = JsonNode.Parse(Sample("setup-attempt"))!;
        wrongAttempt["data"]!["attemptId"] = "other-attempt";
        await FluentActions.Awaiting(() => Client(new Handler(wrongAttempt.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.SetupAttemptGet, Id: "setup-synthetic", SubId: "requested-attempt")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*setup and attempt identities*");

        var wrongList = JsonNode.Parse(Sample("setup-attempt-list"))!;
        wrongList["items"]![0]!["setupId"] = "other-setup";
        await FluentActions.Awaiting(() => Client(new Handler(wrongList.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.SetupAttemptList, Id: "setup")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*another setup*");
    }

    [Fact]
    public async Task AttemptResponsesMustSatisfyThePinnedProducerSchema()
    {
        var missingWatermark = JsonNode.Parse(Sample("setup-attempt-list"))!;
        missingWatermark.AsObject().Remove("snapshotCursor");
        await FluentActions.Awaiting(() => Client(new Handler(missingWatermark.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.SetupAttemptList, Id: "setup-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Unsupported or malformed API response*");

        var unsupportedMode = JsonNode.Parse(Sample("setup-attempt"))!;
        unsupportedMode["data"]!["mode"] = "live";
        await FluentActions.Awaiting(() => Client(new Handler(unsupportedMode.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.SetupAttemptGet, Id: "setup-synthetic", SubId: "attempt-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Unsupported or malformed API response*");
    }

    [Theory]
    [InlineData(404)] [InlineData(409)] [InlineData(412)] [InlineData(422)] [InlineData(428)]
    public async Task CreateRefusalsPreserveHttpStatus(int status)
    {
        var handler = new Handler("{\"code\":\"attempt_refused\"}", (HttpStatusCode)status);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.SetupAttemptCreate,
            Id: "setup", Request: Encoding.UTF8.GetBytes(Sample("setup-attempt-create-request")),
            IdempotencyKey: "key", IfMatch: "\"setup-2\""))).Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be((HttpStatusCode)status);
    }

    [Fact]
    public async Task CommandHelpDocumentsStrongPreconditionsAndFrozenReads()
    {
        var handler = new Handler("{}");
        var create = await Run(handler, ["setup", "attempts", "create", "--help"]);
        create.Exit.Should().Be(0);
        create.Output.Should().Contain("--request").And.Contain("--if-match").And.Contain("--idempotency-key");
        var get = await Run(handler, ["setup", "attempts", "get", "--help"]);
        get.Exit.Should().Be(0);
        get.Output.Should().Contain("<SETUP_ID>").And.Contain("<ATTEMPT_ID>").And.Contain("--cursor");
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
        console.Profile.Width = 500;
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
