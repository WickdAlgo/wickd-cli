using System.Net;
using System.Text;
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

public sealed class PaperHistoryAccountingClientTests
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            Path = request.RequestUri!.PathAndQuery;
            Request = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token);
            Key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
            Match = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            Session = request.Headers.TryGetValues("X-Wickd-Session", out var sessions) ? sessions.Single() : null;
            Workspace = request.Headers.TryGetValues("X-Wickd-Workspace", out var workspaces) ? workspaces.Single() : null;
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
    public async Task HistoryWritesPreserveExactRequestKeyEtagAndAuthenticatedScope()
    {
        foreach (var (operation, requestName, route, etag, predecessor) in new[]
        {
            (V2Operation.PaperHistoryCreate, "paper-history-create-request", "history-attestations", "\"history-revision-3\"", (string?)null),
            (V2Operation.PaperHistoryCorrect, "paper-history-correction-request", "history-attestations/attestation%3Fid/corrections", "\"history-revision-3\"", "attestation?id")
        })
        {
            var response = JsonNode.Parse(Sample("paper-history"))!;
            response["data"]!["walletId"] = "wallet/id";
            response["data"]!["instrumentId"] = "btc/usdt";
            if (operation == V2Operation.PaperHistoryCorrect) response["data"]!["replacesAttestationId"] = predecessor;
            var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
            var bytes = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample(requestName)) + "\n");
            var client = Client(handler);
            var call = new V2Call(operation, Id: "wallet/id", InstrumentId: "btc/usdt", SubId: predecessor,
                Request: bytes, IdempotencyKey: "history-write", IfMatch: etag);

            var first = await client.SendV2Async(call);
            await client.SendV2Async(call);

            first.Status.Should().Be(201);
            handler.Count.Should().Be(2);
            handler.Path.Should().Be($"/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/{route}");
            handler.Request.Should().Equal(bytes);
            handler.Key.Should().Be("history-write");
            handler.Match.Should().Be(etag);
            handler.Session.Should().Be("session-synthetic");
            handler.Workspace.Should().Be("workspace-synthetic");
        }
    }

    [Fact]
    public async Task HistoryAndAccountingReadsPreserveFrozenCursorsAndExactInstrumentScope()
    {
        const string cursor = "snapshot+/=&?";
        var historyList = JsonNode.Parse(Sample("paper-history-list"))!;
        historyList["items"]![0]!["walletId"] = "wallet/id";
        historyList["items"]![0]!["instrumentId"] = "btc/usdt";
        var historyListHandler = new Handler(historyList.ToJsonString());
        await Client(historyListHandler).SendV2Async(new V2Call(V2Operation.PaperHistoryList,
            Id: "wallet/id", InstrumentId: "btc/usdt", Limit: 9, Cursor: cursor));
        historyListHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/history-attestations?limit=9&cursor=snapshot%2B%2F%3D%26%3F");

        var historyDetail = JsonNode.Parse(Sample("paper-history"))!;
        historyDetail["data"]!["walletId"] = "wallet/id";
        historyDetail["data"]!["instrumentId"] = "btc/usdt";
        historyDetail["data"]!["attestationId"] = "history/id";
        var historyDetailHandler = new Handler(historyDetail.ToJsonString());
        await Client(historyDetailHandler).SendV2Async(new V2Call(V2Operation.PaperHistoryGet,
            Id: "wallet/id", InstrumentId: "btc/usdt", SubId: "history/id", Cursor: cursor));
        historyDetailHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/history-attestations/history%2Fid?cursor=snapshot%2B%2F%3D%26%3F");

        var accountingList = JsonNode.Parse(Sample("wallet-accounting-list"))!;
        accountingList["items"]![0]!["walletId"] = "wallet/id";
        accountingList["items"]![0]!["instrumentId"] = "btc/usdt";
        var accountingListHandler = new Handler(accountingList.ToJsonString());
        await Client(accountingListHandler).SendV2Async(new V2Call(V2Operation.WalletAccountingList,
            Id: "wallet/id", InstrumentId: "btc/usdt", Limit: 9, Cursor: cursor));
        accountingListHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/accounting-receipts?limit=9&cursor=snapshot%2B%2F%3D%26%3F");

        var accountingDetail = JsonNode.Parse(Sample("wallet-accounting"))!;
        accountingDetail["data"]!["walletId"] = "wallet/id";
        accountingDetail["data"]!["instrumentId"] = "btc/usdt";
        accountingDetail["data"]!["receiptId"] = "receipt/id";
        var accountingDetailHandler = new Handler(accountingDetail.ToJsonString());
        await Client(accountingDetailHandler).SendV2Async(new V2Call(V2Operation.WalletAccountingGet,
            Id: "wallet/id", InstrumentId: "btc/usdt", SubId: "receipt/id", Cursor: cursor));
        accountingDetailHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/accounting-receipts/receipt%2Fid?cursor=snapshot%2B%2F%3D%26%3F");
    }

    [Fact]
    public async Task AccountingCreateSendsOnlyCanonicalPolicyRequestAndIdempotencyKey()
    {
        var response = JsonNode.Parse(Sample("wallet-accounting"))!;
        response["data"]!["walletId"] = "wallet/id";
        response["data"]!["instrumentId"] = "btc/usdt";
        var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
        var bytes = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample("wallet-accounting-create-request")) + "\n");
        var call = new V2Call(V2Operation.WalletAccountingCreate, Id: "wallet/id", InstrumentId: "btc/usdt",
            Request: bytes, IdempotencyKey: "accounting-key");

        var first = await Client(handler).SendV2Async(call);
        await Client(handler).SendV2Async(call);

        first.Status.Should().Be(201);
        handler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/accounting-receipts");
        handler.Request.Should().Equal(bytes);
        handler.Key.Should().Be("accounting-key");
        handler.Match.Should().BeNull();
    }

    [Fact]
    public async Task AccountingResponsePreservesExactDecimalStringsAndUnknownNulls()
    {
        var response = JsonNode.Parse(Sample("wallet-accounting"))!;
        var handler = new Handler(response.ToJsonString());
        var result = await Client(handler).SendV2Async(new V2Call(V2Operation.WalletAccountingGet,
            Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "receipt-synthetic"));
        var projection = result.Body.GetProperty("data").GetProperty("projection");
        projection.GetProperty("grossRealizedPnl").GetString().Should().Be("0");
        projection.GetProperty("feesTotal").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        projection.GetProperty("netRealizedPnl").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var numeric = JsonNode.Parse(Sample("wallet-accounting"))!;
        numeric["data"]!["projection"]!["grossRealizedPnl"] = 0;
        await FluentActions.Awaiting(() => Client(new Handler(numeric.ToJsonString())).SendV2Async(new V2Call(
            V2Operation.WalletAccountingGet, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "receipt-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*decimal strings*");
    }

    [Fact]
    public async Task WrongWalletInstrumentOrChildIdentityIsRejected()
    {
        var wrongScope = JsonNode.Parse(Sample("wallet-accounting"))!;
        wrongScope["data"]!["instrumentId"] = "other-instrument";
        await FluentActions.Awaiting(() => Client(new Handler(wrongScope.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.WalletAccountingGet, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "receipt-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*wallet/instrument identity*");

        var wrongReceipt = JsonNode.Parse(Sample("wallet-accounting"))!;
        wrongReceipt["data"]!["receiptId"] = "another-receipt";
        await FluentActions.Awaiting(() => Client(new Handler(wrongReceipt.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.WalletAccountingGet, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "receipt-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*receipt identity*");

        var wrongHistory = JsonNode.Parse(Sample("paper-history"))!;
        wrongHistory["data"]!["attestationId"] = "another-attestation";
        await FluentActions.Awaiting(() => Client(new Handler(wrongHistory.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperHistoryGet, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "history-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*attestation identity*");

        wrongHistory = JsonNode.Parse(Sample("paper-history"))!;
        wrongHistory["data"]!["replacesAttestationId"] = "another-attestation";
        await FluentActions.Awaiting(() => Client(new Handler(wrongHistory.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperHistoryCorrect, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "history-synthetic",
                Request: Sample("paper-history-correction-request"), IdempotencyKey: "key", IfMatch: "\"current\"")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*predecessor attestation identity*");

        var wrongList = JsonNode.Parse(Sample("paper-history-list"))!;
        wrongList["items"]![0]!["instrumentId"] = "other-instrument";
        await FluentActions.Awaiting(() => Client(new Handler(wrongList.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperHistoryList, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside the requested wallet/instrument scope*");
    }

    [Fact]
    public async Task WritesRejectBadStrongEtagsAndInvalidPolicyRequestsBeforeNetwork()
    {
        foreach (var etag in new[] { "", "*", "W/\"weak\"", "unquoted" })
        {
            var handler = new Handler(Encoding.UTF8.GetString(Sample("paper-history")));
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.PaperHistoryCreate,
                Id: "wallet", InstrumentId: "instrument", Request: Sample("paper-history-create-request"),
                IdempotencyKey: "key", IfMatch: etag))).Should().ThrowAsync<ArgumentException>();
            handler.Count.Should().Be(0);
        }

        var malformed = Encoding.UTF8.GetBytes("{\"policyVersion\":\"wrong-policy\"}");
        var accounting = new Handler(Encoding.UTF8.GetString(Sample("wallet-accounting")));
        await FluentActions.Awaiting(() => Client(accounting).SendV2Async(new V2Call(V2Operation.WalletAccountingCreate,
            Id: "wallet", InstrumentId: "instrument", Request: malformed, IdempotencyKey: "key")))
            .Should().ThrowAsync<ArgumentException>();
        accounting.Count.Should().Be(0);

        var numeric = Encoding.UTF8.GetString(Sample("wallet-accounting-create-request"))
            .Replace("wallet-fifo-projection-v2", "wallet-fifo-projection-v3", StringComparison.Ordinal);
        await FluentActions.Awaiting(() => Client(accounting).SendV2Async(new V2Call(V2Operation.WalletAccountingCreate,
            Id: "wallet", InstrumentId: "instrument", Request: Encoding.UTF8.GetBytes(numeric), IdempotencyKey: "key")))
            .Should().ThrowAsync<ArgumentException>();
        accounting.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(503)]
    public async Task ErrorsPreserveStatusAndRedactServerBody(int status)
    {
        var handler = new Handler("{\"detail\":\"/private/storage/token=do-not-show\"}", (HttpStatusCode)status);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(
            V2Operation.WalletAccountingList, Id: "wallet", InstrumentId: "instrument")))
            .Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be((HttpStatusCode)status);
        error.Which.Message.Should().Contain(status.ToString()).And.NotContain("private").And.NotContain("do-not-show");
    }

    [Fact]
    public async Task FrozenReadFailuresPreserveHttpStatusAndRedactCursorDetails()
    {
        var handler = new Handler("{\"detail\":\"/private/storage/token=do-not-show\"}", HttpStatusCode.NotFound);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.WalletAccountingGet,
            Id: "wallet", InstrumentId: "instrument", SubId: "receipt", Cursor: "frozen+/cursor")))
            .Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        error.Which.Message.Should().Contain("404").And.NotContain("private").And.NotContain("do-not-show");
        handler.Path.Should().Be("/api/v2/wallets/wallet/instruments/instrument/accounting-receipts/receipt?cursor=frozen%2B%2Fcursor");
    }

    [Theory]
    [InlineData(V2Operation.PaperHistoryCreate, "wallet history correct")]
    [InlineData(V2Operation.PaperHistoryCorrect, "another correction")]
    [InlineData(V2Operation.WalletAccountingCreate, "idempotency key")]
    public async Task ConflictGuidanceMatchesTheHistoryOrAccountingCommand(V2Operation operation, string guidance)
    {
        var handler = new Handler("{\"detail\":\"private server data\"}", HttpStatusCode.Conflict);
        var call = operation switch
        {
            V2Operation.PaperHistoryCreate => new V2Call(operation, Id: "wallet", InstrumentId: "instrument",
                Request: Sample("paper-history-create-request"), IdempotencyKey: "key", IfMatch: "\"current\""),
            V2Operation.PaperHistoryCorrect => new V2Call(operation, Id: "wallet", InstrumentId: "instrument", SubId: "history",
                Request: Sample("paper-history-correction-request"), IdempotencyKey: "key", IfMatch: "\"current\""),
            _ => new V2Call(operation, Id: "wallet", InstrumentId: "instrument",
                Request: Sample("wallet-accounting-create-request"), IdempotencyKey: "key")
        };

        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(call))
            .Should().ThrowAsync<HttpRequestException>();

        error.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        error.Which.Message.Should().Contain(guidance).And.NotContain("private");
    }

    [Fact]
    public async Task HistoryAndAccountingHelpExposeScopedCommandsAndBoundaries()
    {
        var history = await RunHelp(["wallet", "history", "--help"]);
        var accounting = await RunHelp(["wallet", "accounting", "--help"]);
        history.Exit.Should().Be(0);
        history.Output.Should().Contain("create").And.Contain("list").And.Contain("get").And.Contain("correct");
        history.Output.Should().Contain("declared").And.Contain("coverage");
        accounting.Exit.Should().Be(0);
        accounting.Output.Should().Contain("create").And.Contain("list").And.Contain("get");
        accounting.Output.Should().Contain("retained").And.Contain("FIFO");
    }

    [Fact]
    public async Task HistoryAndAccountingReadsRequireCurrentSessionAndWorkspace()
    {
        foreach (var operation in new[]
        {
            V2Operation.PaperHistoryList, V2Operation.PaperHistoryGet,
            V2Operation.WalletAccountingList, V2Operation.WalletAccountingGet
        })
        {
            var body = operation is V2Operation.PaperHistoryList ? "paper-history-list"
                : operation is V2Operation.WalletAccountingList ? "wallet-accounting-list"
                : operation is V2Operation.PaperHistoryGet ? "paper-history" : "wallet-accounting";
            var handler = new Handler(Encoding.UTF8.GetString(Sample(body)));
            var call = new V2Call(operation, Id: "wallet", InstrumentId: "instrument", SubId: "record");
            await FluentActions.Awaiting(() => Client(handler, session: null).SendV2Async(call))
                .Should().ThrowAsync<InvalidOperationException>();
            await FluentActions.Awaiting(() => Client(handler, workspace: null).SendV2Async(call))
                .Should().ThrowAsync<InvalidOperationException>();
            handler.Count.Should().Be(0);
        }
    }

    [Fact]
    public async Task RegisteredHistoryWriteBindsBothScopeIdsAndSendsTheOriginalFile()
    {
        var file = Path.GetTempFileName();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample("paper-history-create-request")) + "\n");
            await File.WriteAllBytesAsync(file, bytes);
            var response = JsonNode.Parse(Sample("paper-history"))!;
            response["data"]!["walletId"] = "wallet/id";
            response["data"]!["instrumentId"] = "btc/usdt";
            var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
            var result = await RunCommand(handler, ["wallet", "history", "create", "wallet/id", "btc/usdt",
                "--request", file, "--if-match", "\"history-current\"", "--idempotency-key", "history-key"]);

            result.Exit.Should().Be(0, result.Output);
            handler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/history-attestations");
            handler.Request.Should().Equal(bytes);
            handler.Key.Should().Be("history-key");
            handler.Match.Should().Be("\"history-current\"");
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

    private static async Task<(int Exit, string Output)> RunHelp(string[] args)
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(output)
        });
        var app = Program.CreateCommandApp(null, console);
        var exit = await app.RunAsync(args);
        return (exit, output.ToString());
    }
}
