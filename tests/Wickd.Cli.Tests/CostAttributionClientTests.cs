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

public sealed class CostAttributionClientTests
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
    public async Task ReadsEncodeRootScopeAndFrozenCursor()
    {
        var list = JsonNode.Parse(Sample("cost-attribution-list"))!;
        list["items"]![0]!["walletId"] = "wallet/id";
        list["items"]![0]!["rootCostId"] = "root/id";
        var handler = new Handler(list.ToJsonString());
        await Client(handler).SendV2Async(new(V2Operation.CostAttributionList, Id: "wallet/id", RootCostId: "root/id", Limit: 7, Cursor: "snap+/=&?"));
        handler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/costs/root%2Fid/attributions?limit=7&cursor=snap%2B%2F%3D%26%3F");
        var detail = JsonNode.Parse(Sample("cost-attribution"))!;
        detail["data"]!["walletId"] = "wallet/id";
        detail["data"]!["rootCostId"] = "root/id";
        detail["data"]!["revisionId"] = "revision/id";
        handler = new Handler(detail.ToJsonString());
        await Client(handler).SendV2Async(new(V2Operation.CostAttributionGet, Id: "wallet/id", RootCostId: "root/id", SubId: "revision/id", Cursor: "snap+/=&?"));
        handler.Path.Should().Be("/api/v2/wallets/wallet%2Fid/costs/root%2Fid/attributions/revision%2Fid?cursor=snap%2B%2F%3D%26%3F");
    }

    [Fact]
    public async Task AssignmentRetainsBytesKeyEtagAndSignedNullableServerMoney()
    {
        var detail = JsonNode.Parse(Sample("cost-attribution"))!;
        detail["data"]!["sourceAmount"] = "-1.2500";
        detail["data"]!["unassignedAmount"] = null;
        detail["data"]!["needsReconciliation"] = true;
        detail["data"]!["unassignedWithheldReason"] = "sourceCostSuperseded";
        detail["data"]!["allocations"]![0]!["amount"] = "-1.00";
        var handler = new Handler(detail.ToJsonString(), HttpStatusCode.Created);
        var request = Encoding.UTF8.GetBytes(" \n" + Encoding.UTF8.GetString(Sample("cost-attribution-request")) + "\n");
        var call = Assign(request);
        var result = await Client(handler).SendV2Async(call);
        await Client(handler).SendV2Async(call);
        handler.Count.Should().Be(2);
        handler.Request.Should().Equal(request);
        handler.Key.Should().Be("retry-key");
        handler.Match.Should().Be("\"current\"");
        result.Body.GetProperty("data").GetProperty("sourceAmount").GetString().Should().Be("-1.2500");
        result.Body.GetProperty("data").GetProperty("unassignedAmount").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        result.Body.GetProperty("data").GetProperty("needsReconciliation").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("walletId")]
    [InlineData("rootCostId")]
    [InlineData("revisionId")]
    public async Task DetailRefusesForeignIdentities(string field)
    {
        var detail = JsonNode.Parse(Sample("cost-attribution"))!;
        detail["data"]![field] = "foreign";
        await FluentActions.Awaiting(() => Client(new Handler(detail.ToJsonString())).SendV2Async(
            new(V2Operation.CostAttributionGet, Id: "wallet-synthetic", RootCostId: "cost-synthetic", SubId: "attribution-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("walletId")]
    [InlineData("rootCostId")]
    public async Task ListRefusesForeignScope(string field)
    {
        var list = JsonNode.Parse(Sample("cost-attribution-list"))!;
        list["items"]![0]![field] = "foreign";
        await FluentActions.Awaiting(() => Client(new Handler(list.ToJsonString())).SendV2Async(
            new(V2Operation.CostAttributionList, Id: "wallet-synthetic", RootCostId: "cost-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("costId", "foreign")]
    [InlineData("priorRevisionId", "foreign")]
    public async Task AssignmentRefusesWrongIntent(string field, string value)
    {
        var detail = JsonNode.Parse(Sample("cost-attribution"))!;
        detail["data"]![field] = value;
        await FluentActions.Awaiting(() => Client(new Handler(detail.ToJsonString())).SendV2Async(Assign(Sample("cost-attribution-request"))))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("W/\"weak\"")]
    [InlineData("unquoted")]
    public async Task AssignmentRefusesInvalidConcurrencyBeforeSending(string? etag)
    {
        var handler = new Handler(Encoding.UTF8.GetString(Sample("cost-attribution")));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(Assign(Sample("cost-attribution-request")) with { IfMatch = etag }))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("1\n")]
    [InlineData("1e0")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("-79228162514264337593543950336")]
    [InlineData("7.9228162514264337593543950336")]
    [InlineData("0.00000000000000000000000000001")]
    public void ContractsRefuseInexactOrMalformedMoney(string value)
    {
        foreach (var field in new[] { "sourceAmount", "unassignedAmount" })
        {
            var detail = JsonNode.Parse(Sample("cost-attribution"))!;
            detail["data"]![field] = value;
            FluentActions.Invoking(() => V2Contracts.Validate(Encoding.UTF8.GetBytes(detail.ToJsonString()), "cost-attribution"))
                .Should().Throw<ArgumentException>();
        }
        var request = JsonNode.Parse(Sample("cost-attribution-request"))!;
        request["allocations"]![0]!["amount"] = value;
        FluentActions.Invoking(() => V2Contracts.Validate(Encoding.UTF8.GetBytes(request.ToJsonString()), "cost-attribution-request"))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.0000")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-7.9228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    [InlineData("79228162514264337593543950335.0")]
    public void ContractsPreserveExactRepresentableMoney(string value)
    {
        var request = JsonNode.Parse(Sample("cost-attribution-request"))!;
        request["allocations"]![0]!["amount"] = value;
        V2Contracts.Validate(Encoding.UTF8.GetBytes(request.ToJsonString()), "cost-attribution-request")
            .GetProperty("allocations")[0].GetProperty("amount").GetString().Should().Be(value);
    }

    [Fact]
    public void NumericMoneyAndOversizedViewsAreRefused()
    {
        var request = JsonNode.Parse(Sample("cost-attribution-request"))!;
        request["allocations"]![0]!["amount"] = 1;
        FluentActions.Invoking(() => V2Contracts.Validate(Encoding.UTF8.GetBytes(request.ToJsonString()), "cost-attribution-request"))
            .Should().Throw<ArgumentException>();
        request = JsonNode.Parse(Sample("cost-attribution-request"))!;
        var rows = new JsonArray();
        for (var i = 0; i < 2001; i++) rows.Add(request["allocations"]![0]!.DeepClone());
        request["allocations"] = rows;
        FluentActions.Invoking(() => V2Contracts.Validate(Encoding.UTF8.GetBytes(request.ToJsonString()), "cost-attribution-request"))
            .Should().Throw<ArgumentException>();
        var list = JsonNode.Parse(Sample("cost-attribution-list"))!;
        list["items"]![0]!["unassignedAmount"] = "1\n";
        FluentActions.Invoking(() => V2Contracts.Validate(Encoding.UTF8.GetBytes(list.ToJsonString()), "cost-attribution-list"))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task UnknownZeroAndEmptyViewsRemainDistinct()
    {
        foreach (var amount in new string?[] { null, "0" })
        {
            var detail = JsonNode.Parse(Sample("cost-attribution"))!;
            detail["data"]!["sourceAmount"] = amount;
            detail["data"]!["unassignedAmount"] = amount;
            detail["data"]!["allocations"] = new JsonArray();
            var response = await Client(new Handler(detail.ToJsonString())).SendV2Async(new(V2Operation.CostAttributionGet,
                Id: "wallet-synthetic", RootCostId: "cost-synthetic", SubId: "attribution-synthetic"));
            response.Body.GetProperty("data").GetProperty("sourceAmount").GetString().Should().Be(amount);
            response.Body.GetProperty("data").GetProperty("allocations").GetArrayLength().Should().Be(0);
        }
        var list = JsonNode.Parse(Sample("cost-attribution-list"))!;
        list["items"] = new JsonArray();
        var empty = await Client(new Handler(list.ToJsonString())).SendV2Async(new(V2Operation.CostAttributionList,
            Id: "wallet-synthetic", RootCostId: "cost-synthetic"));
        empty.Body.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CommandsBindAllThreeOperationsAndRetainLargeRequestBytes()
    {
        var file = Path.GetTempFileName();
        try
        {
            var bytes = Sample("cost-attribution-request");
            bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes) + new string(' ', 1_500_000 - bytes.Length));
            await File.WriteAllBytesAsync(file, bytes);
            var handler = new Handler(Encoding.UTF8.GetString(Sample("cost-attribution")), HttpStatusCode.Created);
            var result = await RunCommand(handler, ["wallet", "cost-attributions", "assign", "wallet-synthetic", "cost-synthetic",
                "--request", file, "--idempotency-key", "retry-key", "--if-match", "\"current\""]);
            result.Exit.Should().Be(0, result.Output);
            handler.Request.Should().Equal(bytes);
            handler.Path.Should().Be("/api/v2/wallets/wallet-synthetic/costs/cost-synthetic/attributions");
            handler.Key.Should().Be("retry-key");
            handler.Match.Should().Be("\"current\"");
            handler = new Handler(Encoding.UTF8.GetString(Sample("cost-attribution-list")));
            result = await RunCommand(handler, ["wallet", "cost-attributions", "list", "wallet-synthetic", "cost-synthetic", "--cursor", "frozen"]);
            result.Exit.Should().Be(0, result.Output);
            handler.Path.Should().EndWith("/attributions?limit=50&cursor=frozen");
            handler = new Handler(Encoding.UTF8.GetString(Sample("cost-attribution")));
            result = await RunCommand(handler, ["wallet", "cost-attributions", "get", "wallet-synthetic", "cost-synthetic", "attribution-synthetic", "--cursor", "frozen"]);
            result.Exit.Should().Be(0, result.Output);
            handler.Path.Should().EndWith("/attributions/attribution-synthetic?cursor=frozen");
            await File.WriteAllBytesAsync(file, new byte[2_097_153]);
            handler = new Handler("{}");
            result = await RunCommand(handler, ["wallet", "cost-attributions", "assign", "wallet-synthetic", "cost-synthetic",
                "--request", file, "--idempotency-key", "key", "--if-match", "\"current\""]);
            result.Exit.Should().Be(2);
            handler.Count.Should().Be(0);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData(409)]
    [InlineData(412)]
    [InlineData(503)]
    public async Task FailuresDoNotEchoServerSecrets(int status)
    {
        var handler = new Handler("{\"secret\":\"must-not-leak\"}", (HttpStatusCode)status);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(Assign(Sample("cost-attribution-request"))))
            .Should().ThrowAsync<HttpRequestException>();
        error.Which.Message.Should().Contain(status.ToString()).And.NotContain("must-not-leak");
    }

    private static V2Call Assign(byte[] bytes) => new(V2Operation.CostAttributionAssign,
        Id: "wallet-synthetic", RootCostId: "cost-synthetic", Request: bytes, IdempotencyKey: "retry-key", IfMatch: "\"current\"");

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
