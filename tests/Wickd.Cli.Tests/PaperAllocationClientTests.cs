using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Tests;

public sealed class PaperAllocationClientTests
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
    public async Task AllocationReadsRetainScopedIdentityPaginationAndFrozenCursor()
    {
        var cursor = "snapshot+/=&?";
        var list = JsonNode.Parse(Sample("paper-allocation-list"))!;
        list["items"]![0]!["walletId"] = "wallet/id";
        list["items"]![0]!["instrumentId"] = "btc/usdt";
        var listHandler = new Handler(list.ToJsonString());
        await Client(listHandler).SendV2Async(new V2Call(V2Operation.PaperAllocationList,
            Id: "wallet/id", InstrumentId: "btc/usdt", Limit: 7, Cursor: cursor));
        listHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/activity-allocations?limit=7&cursor=snapshot%2B%2F%3D%26%3F");

        var detail = JsonNode.Parse(Sample("paper-allocation"))!;
        detail["data"]!["walletId"] = "wallet/id";
        detail["data"]!["instrumentId"] = "btc/usdt";
        detail["data"]!["revisionId"] = "revision/id";
        var detailHandler = new Handler(detail.ToJsonString());
        await Client(detailHandler).SendV2Async(new V2Call(V2Operation.PaperAllocationGet,
            Id: "wallet/id", InstrumentId: "btc/usdt", SubId: "revision/id", Cursor: cursor));
        detailHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/activity-allocations/revision%2Fid?cursor=snapshot%2B%2F%3D%26%3F");
    }

    [Fact]
    public async Task AssignmentPreservesRequestBytesKeyStrongEtagAndServerQuantities()
    {
        var response = JsonNode.Parse(Sample("paper-allocation"))!;
        response["data"]!["walletId"] = "wallet/id";
        response["data"]!["instrumentId"] = "btc/usdt";
        response["data"]!["priorRevisionId"] = "prior-revision";
        var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
        var bytes = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample("paper-allocation-request")) + "\n");
        var request = JsonNode.Parse(bytes)!;
        request["priorRevisionId"] = "prior-revision";
        bytes = Encoding.UTF8.GetBytes(" \n" + request.ToJsonString() + "\n");
        var call = new V2Call(V2Operation.PaperAllocationAssign, Id: "wallet/id", InstrumentId: "btc/usdt",
            Request: bytes, IdempotencyKey: "allocation-retry", IfMatch: "\"allocation-etag\"");

        var first = await Client(handler).SendV2Async(call);
        await Client(handler).SendV2Async(call);

        first.Status.Should().Be(201);
        handler.Count.Should().Be(2);
        handler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/instruments/btc%2Fusdt/activity-allocations");
        handler.Request.Should().Equal(bytes);
        handler.Key.Should().Be("allocation-retry");
        handler.Match.Should().Be("\"allocation-etag\"");
        var revision = first.Body.GetProperty("data");
        revision.GetProperty("unassignedRetainedGraphPortions")[0].GetProperty("unassignedQuantity").GetString().Should().Be("0");
        revision.GetProperty("allocations")[0].GetProperty("grossRealizedPnl").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        revision.GetProperty("feesTotal").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        revision.GetProperty("netRealizedPnl").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task AllocationRevisionAndPredecessorIdentitiesAreBoundToTheRequest()
    {
        var detail = JsonNode.Parse(Sample("paper-allocation"))!;
        detail["data"]!["revisionId"] = "another-revision";
        await FluentActions.Awaiting(() => Client(new Handler(detail.ToJsonString())).SendV2Async(new V2Call(
            V2Operation.PaperAllocationGet, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "expected")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*revision identity*");

        var request = JsonNode.Parse(Sample("paper-allocation-request"))!;
        request["priorRevisionId"] = "requested-predecessor";
        var response = JsonNode.Parse(Sample("paper-allocation"))!;
        response["data"]!["priorRevisionId"] = "different-predecessor";
        await FluentActions.Awaiting(() => Client(new Handler(response.ToJsonString())).SendV2Async(new V2Call(
            V2Operation.PaperAllocationAssign, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic",
            Request: Encoding.UTF8.GetBytes(request.ToJsonString()), IdempotencyKey: "key", IfMatch: "\"current\"")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*predecessor revision identity*");

        var wrongScope = JsonNode.Parse(Sample("paper-allocation-list"))!;
        wrongScope["items"]![0]!["instrumentId"] = "other-instrument";
        await FluentActions.Awaiting(() => Client(new Handler(wrongScope.ToJsonString())).SendV2Async(new V2Call(
            V2Operation.PaperAllocationList, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside the requested wallet/instrument scope*");
    }

    [Fact]
    public async Task AssignmentRejectsMissingWeakOrWildcardEtagsBeforeNetwork()
    {
        foreach (var etag in new[] { "", "*", "W/\"weak\"", "unquoted" })
        {
            var handler = new Handler(Encoding.UTF8.GetString(Sample("paper-allocation")));
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.PaperAllocationAssign,
                Id: "wallet", InstrumentId: "instrument", Request: Sample("paper-allocation-request"),
                IdempotencyKey: "key", IfMatch: etag))).Should().ThrowAsync<ArgumentException>();
            handler.Count.Should().Be(0);
        }
    }

    [Fact]
    public async Task ReconciliationWithholdsNullableUnassignedQuantityWithoutClientCalculation()
    {
        var response = JsonNode.Parse(Sample("paper-allocation"))!;
        response["data"]!["needsReconciliation"] = true;
        response["data"]!["unassignedRetainedGraphPortions"]![0]!["unassignedQuantity"] = null;
        response["data"]!["unassignedRetainedGraphPortions"]![0]!["withheldReason"] = "needsReconciliation";
        var result = await Client(new Handler(response.ToJsonString())).SendV2Async(new V2Call(
            V2Operation.PaperAllocationGet, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "allocation-synthetic"));

        var portion = result.Body.GetProperty("data").GetProperty("unassignedRetainedGraphPortions")[0];
        portion.GetProperty("unassignedQuantity").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        portion.GetProperty("withheldReason").GetString().Should().Be("needsReconciliation");

        response["data"]!["unassignedRetainedGraphPortions"]![0]!["unassignedQuantity"] = 0.25;
        await FluentActions.Awaiting(() => Client(new Handler(response.ToJsonString())).SendV2Async(new V2Call(
            V2Operation.PaperAllocationGet, Id: "wallet-synthetic", InstrumentId: "instrument-synthetic", SubId: "allocation-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*decimal strings*");
    }

    [Fact]
    public async Task AssignmentCommandSendsOriginalRequestWithWalletAndInstrumentScope()
    {
        var file = Path.GetTempFileName();
        try
        {
            var request = JsonNode.Parse(Sample("paper-allocation-request"))!;
            request["priorRevisionId"] = null;
            var bytes = Encoding.UTF8.GetBytes(" \n" + request.ToJsonString() + "\n");
            await File.WriteAllBytesAsync(file, bytes);
            var response = JsonNode.Parse(Sample("paper-allocation"))!;
            response["data"]!["priorRevisionId"] = null;
            var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
            var result = await RunCommand(handler, ["wallet", "allocations", "assign", "wallet-synthetic", "instrument-synthetic",
                "--request", file, "--if-match", "\"current\"", "--idempotency-key", "allocation-key"]);

            result.Exit.Should().Be(0, result.Output);
            handler.Path.Should().Be("/api/v2/wallets/wallet-synthetic/instruments/instrument-synthetic/activity-allocations");
            handler.Request.Should().Equal(bytes);
            handler.Key.Should().Be("allocation-key");
            handler.Match.Should().Be("\"current\"");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task AllocationHelpExposesOnlyTheThreeScopedCommands()
    {
        var help = await RunCommand(new Handler(Encoding.UTF8.GetString(Sample("paper-allocation-list"))),
            ["wallet", "allocations", "--help"]);
        help.Exit.Should().Be(0);
        help.Output.Should().Contain("list").And.Contain("get").And.Contain("assign");
        help.Output.Should().Contain("wallet instrument").And.Contain("attempt");
    }

    [Fact]
    public async Task AllocationFailuresUseSafeServerIndependentGuidance()
    {
        var handler = new Handler("{\"secret\":\"must-not-leak\"}", HttpStatusCode.PreconditionFailed);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(
            V2Operation.PaperAllocationAssign, Id: "wallet", InstrumentId: "instrument",
            Request: Sample("paper-allocation-request"), IdempotencyKey: "key", IfMatch: "\"current\"")))
            .Should().ThrowAsync<HttpRequestException>();
        error.Which.Message.Should().Contain("stale").And.NotContain("must-not-leak");
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
