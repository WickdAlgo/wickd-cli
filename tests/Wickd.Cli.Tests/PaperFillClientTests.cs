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

public sealed class PaperFillClientTests
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

    private sealed class Factory(Handler handler) : IApiClientFactory
    {
        public IWickdApiClient CreateClient(GlobalCommandSettings settings) => Client(handler);
        public WickdCliConfig GetEffectiveConfig(GlobalCommandSettings settings) => new();
    }

    private static WickdApiClient Client(Handler handler, string? session = "session-synthetic", string? workspace = "workspace-synthetic") =>
        new(new HttpClient(handler), new WickdCliConfig
        {
            ApiUrl = "https://example.invalid", SessionToken = session, WorkspaceId = workspace, ApiToken = "service-synthetic"
        });

    private static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(Root(), "contracts", "samples", name + ".v2.sample.json"));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }

    [Fact]
    public async Task CreateAndCorrectionPreserveOriginalBytesAndConcurrencyMetadata()
    {
        foreach (var (operation, requestName, path, etag) in new[]
        {
            (V2Operation.PaperFillCreate, "paper-fill-create-request", "/api/v2/wallets/wallet%2Fid/fills", (string?)null),
            (V2Operation.PaperFillCorrect, "paper-fill-correction-request", "/api/v2/wallets/wallet%2Fid/fills/fill%3Fid/corrections", "\"paper-fills-revision-3\"")
        })
        {
            var response = JsonNode.Parse(Sample("paper-fill").AsSpan())!;
            response["data"]!["walletId"] = "wallet/id";
            if (operation == V2Operation.PaperFillCorrect) response["data"]!["replacesFillId"] = "fill?id";
            var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
            var bytes = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample(requestName)) + "\n");
            var call = new V2Call(operation, Id: "wallet/id", SubId: operation == V2Operation.PaperFillCorrect ? "fill?id" : null,
                Request: bytes, IdempotencyKey: "paper-fill-write", IfMatch: etag);
            var client = Client(handler);

            var first = await client.SendV2Async(call);
            await client.SendV2Async(call);

            first.Status.Should().Be(201);
            handler.Count.Should().Be(2);
            handler.Path.Should().Be(path);
            handler.Request.Should().Equal(bytes);
            handler.Key.Should().Be("paper-fill-write");
            handler.Match.Should().Be(etag);
            handler.Session.Should().Be("session-synthetic");
            handler.Workspace.Should().Be("workspace-synthetic");
            handler.Authorization.Should().Be("Bearer service-synthetic");
        }
    }

    [Fact]
    public async Task ListAndDetailForwardFrozenCursorsAndEncodeWalletFillIds()
    {
        var list = JsonNode.Parse(Sample("paper-fill-list"))!;
        list["items"]![0]!["walletId"] = "wallet/id";
        var listHandler = new Handler(list.ToJsonString());
        var cursor = "snapshot+/=&?";
        await Client(listHandler).SendV2Async(new V2Call(V2Operation.PaperFillList, Id: "wallet/id", Limit: 9, Cursor: cursor));
        listHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/fills?limit=9&cursor=snapshot%2B%2F%3D%26%3F");

        var detail = JsonNode.Parse(Sample("paper-fill"))!;
        detail["data"]!["walletId"] = "wallet/id";
        detail["data"]!["fillId"] = "fill?id";
        var detailHandler = new Handler(detail.ToJsonString());
        await Client(detailHandler).SendV2Async(new V2Call(V2Operation.PaperFillGet,
            Id: "wallet/id", SubId: "fill?id", Cursor: cursor));
        detailHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/fills/fill%3Fid?cursor=snapshot%2B%2F%3D%26%3F");
    }

    [Fact]
    public async Task DecimalFieldsMustRemainStrings()
    {
        var request = Encoding.UTF8.GetString(Sample("paper-fill-create-request")).Replace("\"100.25\"", "100.25", StringComparison.Ordinal);
        var handler = new Handler(Encoding.UTF8.GetString(Sample("paper-fill")));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.PaperFillCreate,
            Id: "wallet-synthetic", Request: Encoding.UTF8.GetBytes(request), IdempotencyKey: "key")))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task ResponsesFromAnotherWalletOrFillAreRefused()
    {
        var wrongWallet = JsonNode.Parse(Sample("paper-fill"))!;
        wrongWallet["data"]!["walletId"] = "other-wallet";
        await FluentActions.Awaiting(() => Client(new Handler(wrongWallet.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperFillGet, Id: "wallet-synthetic", SubId: "fill-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*wallet identity*");

        var wrongFill = JsonNode.Parse(Sample("paper-fill"))!;
        wrongFill["data"]!["fillId"] = "other-fill";
        await FluentActions.Awaiting(() => Client(new Handler(wrongFill.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperFillGet, Id: "wallet-synthetic", SubId: "fill-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*fill identity*");

        var wrongListWallet = JsonNode.Parse(Sample("paper-fill-list"))!;
        wrongListWallet["items"]![0]!["walletId"] = "other-wallet";
        await FluentActions.Awaiting(() => Client(new Handler(wrongListWallet.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperFillList, Id: "wallet-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*another wallet*");

        var wrongPredecessor = JsonNode.Parse(Sample("paper-fill"))!;
        wrongPredecessor["data"]!["replacesFillId"] = "other-fill";
        await FluentActions.Awaiting(() => Client(new Handler(wrongPredecessor.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperFillCorrect, Id: "wallet-synthetic", SubId: "fill-synthetic",
                Request: Sample("paper-fill-correction-request"), IdempotencyKey: "key", IfMatch: "\"current\"")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*predecessor fill identity*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("W/\"weak\"")]
    [InlineData("unquoted")]
    public async Task CorrectionRejectsMissingOrNonStrongEtagBeforeNetwork(string etag)
    {
        var handler = new Handler(Encoding.UTF8.GetString(Sample("paper-fill")));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.PaperFillCorrect,
            Id: "wallet-synthetic", SubId: "fill-synthetic", Request: Sample("paper-fill-correction-request"),
            IdempotencyKey: "correction-key", IfMatch: etag)))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(503)]
    public async Task ErrorsKeepStatusAndRedactServerBodyAndSecrets(int status)
    {
        var handler = new Handler("{\"detail\":\"/private/storage/token=do-not-show\"}", (HttpStatusCode)status);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(
            new V2Call(V2Operation.PaperFillList, Id: "wallet-synthetic")))
            .Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be((HttpStatusCode)status);
        error.Which.Message.Should().Contain(status.ToString()).And.NotContain("private").And.NotContain("do-not-show");
    }

    [Fact]
    public async Task PaperFillReadsRequireCurrentSessionAndWorkspaceBeforeNetwork()
    {
        var handler = new Handler(Encoding.UTF8.GetString(Sample("paper-fill-list")));
        await FluentActions.Awaiting(() => Client(handler, session: null).SendV2Async(new V2Call(V2Operation.PaperFillList, Id: "wallet")))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(handler, workspace: null).SendV2Async(new V2Call(V2Operation.PaperFillList, Id: "wallet")))
            .Should().ThrowAsync<InvalidOperationException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task RegisteredCorrectionCommandBindsWalletFillIdRequestKeyAndEtag()
    {
        var file = Path.GetTempFileName();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample("paper-fill-correction-request")) + "\n");
            await File.WriteAllBytesAsync(file, bytes);
            var response = JsonNode.Parse(Sample("paper-fill"))!;
            response["data"]!["walletId"] = "wallet/id";
            response["data"]!["replacesFillId"] = "fill?id";
            var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
            var result = await RunCommand(handler, ["wallet", "fills", "correct", "wallet/id", "fill?id",
                "--request", file, "--if-match", "\"paper-fill-current\"", "--idempotency-key", "correction-key"]);

            result.Exit.Should().Be(0, result.Output);
            handler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/fills/fill%3Fid/corrections");
            handler.Request.Should().Equal(bytes);
            handler.Key.Should().Be("correction-key");
            handler.Match.Should().Be("\"paper-fill-current\"");
        }
        finally { File.Delete(file); }
    }

    private static async Task<(int Exit, string Output)> RunCommand(Handler handler, string[] args)
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output)
        });
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
