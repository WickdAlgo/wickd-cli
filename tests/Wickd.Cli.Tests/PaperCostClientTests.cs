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

public sealed class PaperCostClientTests
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

    private static WickdApiClient Client(Handler handler) => new(new HttpClient(handler), new WickdCliConfig
    {
        ApiUrl = "https://example.invalid", SessionToken = "session-synthetic", WorkspaceId = "workspace-synthetic", ApiToken = "service-synthetic"
    });

    private static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(Root(), "contracts", "samples", name + ".v2.sample.json"));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }

    [Fact]
    public async Task CreatePreservesExactNullableRequestBytesAndExplicitRetryKeyWithoutEtag()
    {
        var response = JsonNode.Parse(Sample("paper-cost"))!;
        response["data"]!["walletId"] = "wallet/id";
        response["data"]!["amount"] = null;
        var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
        var bytes = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample("paper-cost-create-request"))
            .Replace("\"amount\":\"1.25\"", "\"amount\":null", StringComparison.Ordinal) + "\n");
        var call = new V2Call(V2Operation.PaperCostCreate, Id: "wallet/id", Request: bytes, IdempotencyKey: "cost-retry");

        var first = await Client(handler).SendV2Async(call);
        await Client(handler).SendV2Async(call);

        first.Status.Should().Be(201);
        handler.Count.Should().Be(2);
        handler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/costs");
        handler.Request.Should().Equal(bytes);
        handler.Key.Should().Be("cost-retry");
        handler.Match.Should().BeNull();
        var cost = first.Body.GetProperty("data");
        cost.GetProperty("amount").ValueKind.Should().Be(JsonValueKind.Null);
        cost.GetProperty("costCoverage").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task CorrectionRequiresStrongWalletEtagAndBindsPredecessorCost()
    {
        var response = JsonNode.Parse(Sample("paper-cost"))!;
        response["data"]!["walletId"] = "wallet/id";
        response["data"]!["replacesCostId"] = "cost/id";
        var handler = new Handler(response.ToJsonString(), HttpStatusCode.Created);
        var bytes = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample("paper-cost-correction-request")) + "\n");
        var call = new V2Call(V2Operation.PaperCostCorrect, Id: "wallet/id", SubId: "cost/id", Request: bytes,
            IdempotencyKey: "correction-key", IfMatch: "\"paper-costs-3\"");

        var result = await Client(handler).SendV2Async(call);

        result.Status.Should().Be(201);
        handler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/costs/cost%2Fid/corrections");
        handler.Request.Should().Equal(bytes);
        handler.Key.Should().Be("correction-key");
        handler.Match.Should().Be("\"paper-costs-3\"");
        result.Body.GetProperty("data").GetProperty("replacesCostId").GetString().Should().Be("cost/id");
        result.Body.GetProperty("data").GetProperty("amount").GetString().Should().Be("1.25");
    }

    [Fact]
    public async Task ListAndGetKeepWalletScopeAndSignedSnapshotCursorOpaque()
    {
        const string cursor = "snapshot+/=&?";
        var list = JsonNode.Parse(Sample("paper-cost-list"))!;
        list["items"]![0]!["walletId"] = "wallet/id";
        var listHandler = new Handler(list.ToJsonString());
        await Client(listHandler).SendV2Async(new V2Call(V2Operation.PaperCostList, Id: "wallet/id", Limit: 7, Cursor: cursor));
        listHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/costs?limit=7&cursor=snapshot%2B%2F%3D%26%3F");

        var detail = JsonNode.Parse(Sample("paper-cost"))!;
        detail["data"]!["walletId"] = "wallet/id";
        detail["data"]!["costId"] = "cost/id";
        var detailHandler = new Handler(detail.ToJsonString());
        await Client(detailHandler).SendV2Async(new V2Call(V2Operation.PaperCostGet, Id: "wallet/id", SubId: "cost/id", Cursor: cursor));
        detailHandler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/costs/cost%2Fid?cursor=snapshot%2B%2F%3D%26%3F");

        var foreign = JsonNode.Parse(Sample("paper-cost-list"))!;
        foreign["items"]![0]!["walletId"] = "another-wallet";
        await FluentActions.Awaiting(() => Client(new Handler(foreign.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperCostList, Id: "wallet-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*another wallet*");
    }

    [Fact]
    public async Task CostReadsRejectWrongWalletAndCostIdentityAndNumericAmounts()
    {
        var wrongWallet = JsonNode.Parse(Sample("paper-cost"))!;
        wrongWallet["data"]!["walletId"] = "another-wallet";
        await FluentActions.Awaiting(() => Client(new Handler(wrongWallet.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperCostGet, Id: "wallet-synthetic", SubId: "cost-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*wallet identity*");

        var wrongCost = JsonNode.Parse(Sample("paper-cost"))!;
        wrongCost["data"]!["costId"] = "another-cost";
        await FluentActions.Awaiting(() => Client(new Handler(wrongCost.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperCostGet, Id: "wallet-synthetic", SubId: "cost-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*cost identity*");

        var wrongPredecessor = JsonNode.Parse(Sample("paper-cost"))!;
        wrongPredecessor["data"]!["replacesCostId"] = "another-cost";
        await FluentActions.Awaiting(() => Client(new Handler(wrongPredecessor.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperCostCorrect, Id: "wallet-synthetic", SubId: "cost-synthetic",
                Request: Sample("paper-cost-correction-request"), IdempotencyKey: "key", IfMatch: "\"current\"")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*predecessor cost identity*");

        var numeric = JsonNode.Parse(Sample("paper-cost"))!;
        numeric["data"]!["amount"] = 1.25;
        await FluentActions.Awaiting(() => Client(new Handler(numeric.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperCostGet, Id: "wallet-synthetic", SubId: "cost-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*decimal strings*");

        var newline = JsonNode.Parse(Sample("paper-cost"))!;
        newline["data"]!["amount"] = "1.25\n";
        await FluentActions.Awaiting(() => Client(new Handler(newline.ToJsonString())).SendV2Async(
            new V2Call(V2Operation.PaperCostGet, Id: "wallet-synthetic", SubId: "cost-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*line breaks*");
    }

    [Fact]
    public async Task RequestSchemasAcceptNullableAmountsButRejectJsonNumbersAndNewlineDecimals()
    {
        var nullRequest = Encoding.UTF8.GetString(Sample("paper-cost-create-request"))
            .Replace("\"amount\":\"1.25\"", "\"amount\":null", StringComparison.Ordinal);
        var valid = new Handler(Encoding.UTF8.GetString(Sample("paper-cost")));
        await Client(valid).SendV2Async(new V2Call(V2Operation.PaperCostCreate, Id: "wallet-synthetic",
            Request: Encoding.UTF8.GetBytes(nullRequest), IdempotencyKey: "valid-null"));
        valid.Count.Should().Be(1);

        var invalidCreateRequests = new[] { nullRequest.Replace("\"amount\":null", "\"amount\":1.25", StringComparison.Ordinal) }
            .Concat(new[] { "1.25\n", "1.25\r", "1.25\r\n" }.Select(amount => nullRequest.Replace(
                "\"amount\":null", "\"amount\":" + JsonSerializer.Serialize(amount), StringComparison.Ordinal)));
        foreach (var invalid in invalidCreateRequests)
        {
            var handler = new Handler(Encoding.UTF8.GetString(Sample("paper-cost")));
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.PaperCostCreate,
                Id: "wallet-synthetic", Request: Encoding.UTF8.GetBytes(invalid), IdempotencyKey: "invalid")))
                .Should().ThrowAsync<ArgumentException>();
            handler.Count.Should().Be(0);
        }

        var correctionBytes = Encoding.UTF8.GetString(Sample("paper-cost-correction-request"));
        var correctionResponse = JsonNode.Parse(Sample("paper-cost"))!;
        correctionResponse["data"]!["replacesCostId"] = "cost-synthetic";
        var validCorrection = new Handler(correctionResponse.ToJsonString(), HttpStatusCode.Created);
        await Client(validCorrection).SendV2Async(new V2Call(V2Operation.PaperCostCorrect, Id: "wallet-synthetic", SubId: "cost-synthetic",
            Request: Encoding.UTF8.GetBytes(correctionBytes), IdempotencyKey: "valid-null-correction", IfMatch: "\"current\""));
        validCorrection.Count.Should().Be(1);

        var invalidCorrectionRequests = new[] { correctionBytes.Replace("\"amount\":null", "\"amount\":1.25", StringComparison.Ordinal) }
            .Concat(new[] { "1.25\n", "1.25\r", "1.25\r\n" }.Select(amount => correctionBytes.Replace(
                "\"amount\":null", "\"amount\":" + JsonSerializer.Serialize(amount), StringComparison.Ordinal)));
        foreach (var invalid in invalidCorrectionRequests)
        {
            var handler = new Handler(correctionResponse.ToJsonString(), HttpStatusCode.Created);
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.PaperCostCorrect,
                Id: "wallet-synthetic", SubId: "cost-synthetic", Request: Encoding.UTF8.GetBytes(invalid),
                IdempotencyKey: "invalid-correction", IfMatch: "\"current\"")))
                .Should().ThrowAsync<ArgumentException>();
            handler.Count.Should().Be(0);
        }

        foreach (var amount in new[] { "-1.25", "0" })
        {
            var signedRequest = nullRequest.Replace("\"amount\":null", $"\"amount\":\"{amount}\"", StringComparison.Ordinal);
            var signedResponse = JsonNode.Parse(Sample("paper-cost"))!;
            signedResponse["data"]!["amount"] = amount;
            var handler = new Handler(signedResponse.ToJsonString(), HttpStatusCode.Created);
            var result = await Client(handler).SendV2Async(new V2Call(V2Operation.PaperCostCreate, Id: "wallet-synthetic",
                Request: Encoding.UTF8.GetBytes(signedRequest), IdempotencyKey: "signed-" + amount));
            result.Body.GetProperty("data").GetProperty("amount").GetString().Should().Be(amount);
        }
    }

    [Fact]
    public async Task CorrectionRejectsWeakWildcardOrMissingEtagsBeforeNetwork()
    {
        foreach (var etag in new[] { "", "*", "W/\"weak\"", "unquoted" })
        {
            var handler = new Handler(Encoding.UTF8.GetString(Sample("paper-cost")));
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.PaperCostCorrect,
                Id: "wallet", SubId: "cost", Request: Sample("paper-cost-correction-request"),
                IdempotencyKey: "key", IfMatch: etag))).Should().ThrowAsync<ArgumentException>();
            handler.Count.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(412)]
    [InlineData(503)]
    public async Task CostErrorsKeepStatusAndHideServerDetails(int status)
    {
        var handler = new Handler("{\"detail\":\"/private/storage/token=do-not-show\"}", (HttpStatusCode)status);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(
            new V2Call(V2Operation.PaperCostList, Id: "wallet"))).Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be((HttpStatusCode)status);
        error.Which.Message.Should().NotContain("private").And.NotContain("do-not-show");
    }

    [Fact]
    public async Task CostsHelpListsOnlyScopedCreateListGetAndCorrectCommands()
    {
        var result = await RunCommand(new Handler(Encoding.UTF8.GetString(Sample("paper-cost-list"))), ["wallet", "costs", "--help"]);
        result.Exit.Should().Be(0);
        result.Output.Should().Contain("create").And.Contain("list").And.Contain("get").And.Contain("correct");
        result.Output.Should().Contain("wallet").And.Contain("cost");
    }

    [Fact]
    public async Task CostCommandsBindWalletAndCostArgumentsAndForwardCorrectionControls()
    {
        var createFile = Path.GetTempFileName();
        var correctionFile = Path.GetTempFileName();
        try
        {
            var createBytes = Sample("paper-cost-create-request");
            var correctionBytes = Sample("paper-cost-correction-request");
            await File.WriteAllBytesAsync(createFile, createBytes);
            await File.WriteAllBytesAsync(correctionFile, correctionBytes);

            var created = JsonNode.Parse(Sample("paper-cost"))!;
            created["data"]!["walletId"] = "wallet-synthetic";
            var createHandler = new Handler(created.ToJsonString(), HttpStatusCode.Created);
            var create = await RunCommand(createHandler, ["wallet", "costs", "create", "wallet-synthetic",
                "--request", createFile, "--idempotency-key", "create-key"]);
            create.Exit.Should().Be(0, create.Output);
            createHandler.Path.Should().Be("/api/v2/wallets/wallet-synthetic/costs");
            createHandler.Request.Should().Equal(createBytes);
            createHandler.Key.Should().Be("create-key");
            createHandler.Match.Should().BeNull();

            var corrected = JsonNode.Parse(Sample("paper-cost"))!;
            corrected["data"]!["walletId"] = "wallet-synthetic";
            corrected["data"]!["replacesCostId"] = "cost-synthetic";
            var correctionHandler = new Handler(corrected.ToJsonString(), HttpStatusCode.Created);
            var correction = await RunCommand(correctionHandler, ["wallet", "costs", "correct", "wallet-synthetic", "cost-synthetic",
                "--request", correctionFile, "--if-match", "\"cost-stream-2\"", "--idempotency-key", "correct-key"]);
            correction.Exit.Should().Be(0, correction.Output);
            correctionHandler.Path.Should().Be("/api/v2/wallets/wallet-synthetic/costs/cost-synthetic/corrections");
            correctionHandler.Request.Should().Equal(correctionBytes);
            correctionHandler.Key.Should().Be("correct-key");
            correctionHandler.Match.Should().Be("\"cost-stream-2\"");

            var listed = JsonNode.Parse(Sample("paper-cost-list"))!;
            listed["items"]![0]!["walletId"] = "wallet-synthetic";
            var listHandler = new Handler(listed.ToJsonString());
            var list = await RunCommand(listHandler, ["wallet", "costs", "list", "wallet-synthetic"]);
            list.Exit.Should().Be(0, list.Output);
            listHandler.Path.Should().Be("/api/v2/wallets/wallet-synthetic/costs?limit=50");

            var detail = JsonNode.Parse(Sample("paper-cost"))!;
            detail["data"]!["walletId"] = "wallet-synthetic";
            var getHandler = new Handler(detail.ToJsonString());
            var get = await RunCommand(getHandler, ["wallet", "costs", "get", "wallet-synthetic", "cost-synthetic"]);
            get.Exit.Should().Be(0, get.Output);
            getHandler.Path.Should().Be("/api/v2/wallets/wallet-synthetic/costs/cost-synthetic");
        }
        finally
        {
            File.Delete(createFile);
            File.Delete(correctionFile);
        }
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
