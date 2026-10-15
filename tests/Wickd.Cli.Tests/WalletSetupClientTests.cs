using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Wickd.Cli.Common;
using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Tests;

public sealed class WalletSetupClientTests
{
    private sealed class Handler(string sample, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public byte[]? Body { get; private set; }
        public Dictionary<string, string> Headers { get; } = [];
        public int Count { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            Path = request.RequestUri!.PathAndQuery;
            Body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token);
            foreach (var header in request.Headers) Headers[header.Key] = string.Join(",", header.Value);
            var response = new HttpResponseMessage(status) { Content = new StringContent(sample, Encoding.UTF8, "application/json") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"setup-3\"");
            response.Headers.Location = new Uri("/api/v2/setups/setup-synthetic", UriKind.Relative);
            return response;
        }
    }

    private static WickdApiClient Client(Handler handler, string? session = "redacted-session", string? workspace = "workspace-synthetic") =>
        new(new HttpClient(handler), new WickdCliConfig { ApiUrl = "https://example.invalid", ApiToken = "redacted-host",
            SessionToken = session, WorkspaceId = workspace });

    private static string Sample(string contract) => File.ReadAllText(Path.Combine(Root(), "contracts", "samples", contract + ".v2.sample.json"));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }

    public static TheoryData<V2Operation, string, string> Reads => new()
    {
        { V2Operation.CatalogAssets, "catalog-assets", "/api/v2/catalog/assets" },
        { V2Operation.CatalogInstruments, "catalog-instruments", "/api/v2/catalog/instruments" },
        { V2Operation.CatalogSpecs, "instrument-spec-versions", "/api/v2/catalog/instrument-spec-versions" },
        { V2Operation.CatalogTimeframes, "timeframe-definitions", "/api/v2/catalog/timeframes" },
        { V2Operation.WalletList, "wallet-list", "/api/v2/wallets?limit=50" },
        { V2Operation.WalletGet, "wallet-detail", "/api/v2/wallets/a%2Fb%3Fc" },
        { V2Operation.WalletBalance, "wallet-balance", "/api/v2/wallets/a%2Fb%3Fc/balance" },
        { V2Operation.SetupList, "journal-setup-list", "/api/v2/setups?limit=50" },
        { V2Operation.SetupGet, "journal-setup", "/api/v2/setups/a%2Fb%3Fc" }
    };

    [Theory, MemberData(nameof(Reads))]
    public async Task ReadsConsumeGeneratedSamplesAndPreserveMetadata(V2Operation operation, string contract, string expectedPath)
    {
        var handler = new Handler(Sample(contract));
        var response = await Client(handler).SendV2Async(new V2Call(operation, Id: "a/b?c"));
        handler.Path.Should().Be(expectedPath);
        handler.Headers["X-Wickd-Session"].Should().Be("redacted-session");
        handler.Headers["X-Wickd-Workspace"].Should().Be("workspace-synthetic");
        response.ETag.Should().Be("\"setup-3\"");
        response.Location.Should().Be("/api/v2/setups/setup-synthetic");
        response.Body.GetProperty("contract").GetString().Should().Be(contract);
        response.Body.GetRawText().Should().Be(Sample(contract).Trim());
    }

    public static TheoryData<V2Operation, string, string, string> Writes => new()
    {
        { V2Operation.WalletCreate, "wallet-create-request", "wallet-detail", "/api/v2/wallets" },
        { V2Operation.RiskPreview, "risk-preview-request", "risk-preview", "/api/v2/risk/previews" },
        { V2Operation.SetupAccept, "setup-accept-request", "journal-setup", "/api/v2/setups" },
        { V2Operation.SetupAmend, "setup-amend-request", "journal-setup", "/api/v2/setups/setup%2Fid/plan-revisions" }
    };

    [Theory, MemberData(nameof(Writes))]
    public async Task WritesRetainExactRequestBytesForSafeRetry(V2Operation operation, string requestContract, string responseContract, string path)
    {
        var handler = new Handler(Sample(responseContract), HttpStatusCode.Created);
        var bytes = Encoding.UTF8.GetBytes(" \n" + Sample(requestContract));
        var call = new V2Call(operation, Id: "setup/id", Request: bytes, IdempotencyKey: "same-key", IfMatch: "\"setup-2\"");
        var client = Client(handler);
        var first = await client.SendV2Async(call);
        await client.SendV2Async(call);
        handler.Count.Should().Be(2);
        handler.Body.Should().Equal(bytes);
        handler.Path.Should().Be(path);
        handler.Headers["Idempotency-Key"].Should().Be("same-key");
        if (operation == V2Operation.SetupAmend) handler.Headers["If-Match"].Should().Be("\"setup-2\"");
        first.Status.Should().Be(201);
    }

    [Fact]
    public async Task CatalogRequiresSessionButNoWorkspace()
    {
        var handler = new Handler(Sample("catalog-assets"));
        await Client(handler, workspace: null).SendV2Async(new V2Call(V2Operation.CatalogAssets));
        handler.Count.Should().Be(1);
        await FluentActions.Awaiting(() => Client(handler, session: null).SendV2Async(new V2Call(V2Operation.CatalogAssets)))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(handler, workspace: null).SendV2Async(new V2Call(V2Operation.WalletList)))
            .Should().ThrowAsync<InvalidOperationException>();
        handler.Count.Should().Be(1);
    }

    [Fact]
    public async Task CursorIsOpaqueEncodedAndOnePageIsReturned()
    {
        var handler = new Handler(Sample("wallet-list"));
        var result = await Client(handler).SendV2Async(new V2Call(V2Operation.WalletList, Cursor: "signed+/=&?", Limit: 7));
        handler.Path.Should().Be("/api/v2/wallets?limit=7&cursor=signed%2B%2F%3D%26%3F");
        result.Body.GetProperty("publicationWatermark").GetInt64().Should().BeGreaterThan(0);
        handler.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(404)] [InlineData(409)]
    [InlineData(412)] [InlineData(422)] [InlineData(428)] [InlineData(503)]
    public async Task ErrorsPreserveExactStatusAndProblemBody(int code)
    {
        var handler = new Handler("{\"code\":\"synthetic_refusal\",\"detail\":\"Request refused\"}", (HttpStatusCode)code);
        var exception = await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.SetupGet, Id: "setup")))
            .Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be((HttpStatusCode)code);
        exception.Which.Message.Should().Contain("synthetic_refusal").And.Contain(code.ToString());
        handler.Count.Should().Be(1);
    }

    [Theory]
    [InlineData("")] [InlineData("*")] [InlineData("W/\"setup-2\"")] [InlineData("setup-2")] [InlineData("\"a\", \"b\"")]
    [InlineData("\"setup-2\"\r\nInjected: value")]
    public async Task AmendmentRequiresSingleStrongEtag(string etag)
    {
        var handler = new Handler(Sample("journal-setup"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.SetupAmend, Id: "s",
            Request: Encoding.UTF8.GetBytes(Sample("setup-amend-request")), IdempotencyKey: "key", IfMatch: etag)))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("")] [InlineData(" key")] [InlineData("key\nInjected: value")]
    public async Task WritesRequireExplicitSafeIdempotencyKey(string key)
    {
        var handler = new Handler(Sample("wallet-detail"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.WalletCreate,
            Request: Encoding.UTF8.GetBytes(Sample("wallet-create-request")), IdempotencyKey: key)))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(".")] [InlineData("..")] [InlineData("s\r\n")]
    public async Task UnsafeIdentifiersNeverReachNetwork(string id)
    {
        var handler = new Handler(Sample("journal-setup"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.SetupGet, Id: id)))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(0, null)] [InlineData(101, null)] [InlineData(50, "")] [InlineData(50, "x\r\nInjected: value")]
    public async Task InvalidPaginationIsRejectedBeforeNetwork(int limit, string? cursor)
    {
        var handler = new Handler(Sample("wallet-list"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.WalletList, Limit: limit, Cursor: cursor)))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public void GeneratedRequestSchemasRejectNumericMoneyAndIncompletePlans()
    {
        var valid = Sample("risk-preview-request");
        Action numeric = () => V2Contracts.Validate(Encoding.UTF8.GetBytes(valid.Replace("\"50000\"", "50000")), "risk-preview-request");
        numeric.Should().Throw<ArgumentException>();
        Action missing = () => V2Contracts.Validate("{}"u8.ToArray(), "risk-preview-request");
        missing.Should().Throw<ArgumentException>();
        Action date = () => V2Contracts.Validate(Encoding.UTF8.GetBytes(valid.Replace("2026-10-14T08:00:00+00:00", "yesterday")), "risk-preview-request");
        date.Should().Throw<ArgumentException>();
        Action duplicate = () => V2Contracts.Validate(Encoding.UTF8.GetBytes(Sample("wallet-create-request").Replace("\"kind\":\"paper\"", "\"kind\":\"paper\",\"kind\":\"backtest\"")), "wallet-create-request");
        duplicate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task MalformedFinancialRequestIsRejectedBeforeNetwork()
    {
        var handler = new Handler(Sample("risk-preview"));
        var bytes = Encoding.UTF8.GetBytes(Sample("risk-preview-request").Replace("\"50000\"", "50000"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.RiskPreview,
            Request: bytes, IdempotencyKey: "invalid-money"))).Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task UnexpectedAndMalformedResponseContractsAreRejected()
    {
        foreach (var sample in new[] { "{broken", Sample("wallet-detail").Replace("\"wallet-detail\"", "\"wallet-other\""), "{\"schemaVersion\":2,\"contract\":\"wallet-detail\"}" })
        {
            var handler = new Handler(sample);
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.WalletGet, Id: "wallet")))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }
    [Fact]
    public async Task FinancialStringsAreNeverRoundedOnRequestOrResponse()
    {
        const string exact = "0.1234567890123456789012345678";
        var request = Encoding.UTF8.GetBytes(Sample("wallet-create-request").Replace("\"10000\"", "\"" + exact + "\""));
        var sample = Sample("wallet-detail").Replace("\"10000\"", "\"" + exact + "\"");
        var handler = new Handler(sample);
        var response = await Client(handler).SendV2Async(new V2Call(V2Operation.WalletCreate, Request: request, IdempotencyKey: "exact-key"));
        handler.Body.Should().Equal(request);
        response.Body.GetProperty("data").GetProperty("openingFacts")[0].GetProperty("quantity").GetString().Should().Be(exact);
    }

    private sealed class Factory(Handler handler) : IApiClientFactory
    {
        public IWickdApiClient CreateClient(GlobalCommandSettings settings) => Client(handler);
        public WickdCliConfig GetEffectiveConfig(GlobalCommandSettings settings) => new();
    }

    [Theory]
    [InlineData("catalog", "assets", "catalog-assets")]
    [InlineData("catalog", "instruments", "catalog-instruments")]
    [InlineData("catalog", "specs", "instrument-spec-versions")]
    [InlineData("catalog", "timeframes", "timeframe-definitions")]
    [InlineData("wallet", "list", "wallet-list")]
    [InlineData("wallet", "get", "wallet-detail")]
    [InlineData("wallet", "balance", "wallet-balance")]
    [InlineData("setup", "list", "journal-setup-list")]
    [InlineData("setup", "get", "journal-setup")]
    public async Task RegisteredReadCommandsRenderExactServerData(string branch, string command, string contract)
    {
        var args = new List<string> { branch, command };
        if (command is "get" or "balance") args.Add("identity");
        var handler = new Handler(Sample(contract));
        var result = await RunCommand(handler, args.ToArray());
        result.Exit.Should().Be(0);
        result.Output.Should().Contain(contract).And.Contain("eTag").And.Contain("setup-3");
        handler.Count.Should().Be(1);
    }

    [Theory]
    [InlineData("wallet", "create", "wallet-create-request", "wallet-detail")]
    [InlineData("risk", "preview", "risk-preview-request", "risk-preview")]
    [InlineData("setup", "accept", "setup-accept-request", "journal-setup")]
    [InlineData("setup", "amend", "setup-amend-request", "journal-setup")]
    public async Task RegisteredWriteCommandsUseFilesAndExplicitConcurrencyHeaders(string branch, string command, string requestContract, string responseContract)
    {
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, Sample(requestContract));
            var args = new List<string> { branch, command };
            if (command == "amend") args.AddRange(["setup-synthetic", "--if-match", "\"setup-2\""]);
            args.AddRange(["--request", file, "--idempotency-key", "command-key"]);
            var handler = new Handler(Sample(responseContract), HttpStatusCode.Created);
            var result = await RunCommand(handler, args.ToArray());
            result.Exit.Should().Be(0);
            result.Output.Should().Contain("201");
            handler.Body.Should().Equal(await File.ReadAllBytesAsync(file));
            handler.Headers["Idempotency-Key"].Should().Be("command-key");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task CommandRejectsMissingFileBeforeNetwork()
    {
        var handler = new Handler(Sample("wallet-detail"));
        var result = await RunCommand(handler, ["wallet", "create", "--request", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), "--idempotency-key", "key"]);
        result.Exit.Should().Be(2);
        handler.Count.Should().Be(0);
    }

    private static async Task<(int Exit, string Output)> RunCommand(Handler handler, string[] args)
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No, Out = new AnsiConsoleOutput(output) });
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
