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

/// <summary>Authenticates and binds immutable server capital evidence without computing a wallet balance.</summary>
public sealed class WalletCapitalClientTests
{
    private sealed class Handler(string body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<byte[]?> Requests { get; } = [];
        public List<string?> Keys { get; } = [];
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string? ETag { get; init; } = "\"wallet-capital-capital-synthetic\"";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            request.Headers.GetValues("X-Wickd-Workspace").Single().Should().Be("workspace");
            request.Headers.GetValues("X-Wickd-Session").Single().Should().Be("session");
            request.Headers.Authorization!.ToString().Should().Be("Bearer service");
            Requests.Add(request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token));
            Keys.Add(request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null);
            var result = new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (ETag is not null) result.Headers.TryAddWithoutValidation("ETag", ETag);
            return result;
        }
    }
    private static WickdApiClient Client(Handler handler, string? session = "session", string? workspace = "workspace") => new(new HttpClient(handler), new WickdCliConfig
    { ApiUrl = "https://example.invalid", ApiToken = "service", SessionToken = session, WorkspaceId = workspace });
    private static string Sample(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "contracts", "samples", name + ".v2.sample.json"));
    }
    private static V2Call Create(byte[]? bytes = null) => new(V2Operation.WalletCapitalCreate, Id: "wallet-synthetic",
        Request: bytes ?? Encoding.UTF8.GetBytes(Sample("wallet-capital-create-request")), IdempotencyKey: "stable-capital");

    [Fact]
    public async Task CreateKeepsOriginalBytesKeyAndExactServerDecimalsOnRetry()
    {
        var bytes = Encoding.UTF8.GetBytes("\n" + Sample("wallet-capital-create-request") + " \n");
        var handler = new Handler(Sample("wallet-capital")) { Status = HttpStatusCode.Created };
        var result = await Client(handler).SendV2Async(Create(bytes));
        await Client(handler).SendV2Async(Create(bytes));
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Should().OnlyContain(b => b!.SequenceEqual(bytes));
        handler.Keys.Should().Equal("stable-capital", "stable-capital");
        handler.Paths.Should().OnlyContain(p => p == "/api/v2/wallets/wallet-synthetic/capital-movements");
        result.Body.GetProperty("data").GetProperty("amount").GetString().Should().Be("125.25");
        result.Body.GetProperty("data").GetProperty("source").GetString().Should().Be("userDeclaredPaperCapital");
    }

    [Fact]
    public async Task CreateAcceptsTrimmedReasonAndOffsetEquivalentEconomicInstant()
    {
        var request = JsonNode.Parse(Sample("wallet-capital-create-request"))!;
        request["reason"] = "  Synthetic external capital  ";
        request["occurredAtUtc"] = "2026-10-14T13:30:00+05:30";
        await Client(new Handler(Sample("wallet-capital"))).SendV2Async(Create(Encoding.UTF8.GetBytes(request.ToJsonString())));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1\n")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("0.00000000000000000000000000001")]
    public async Task InvalidCapitalAmountIsRejectedBeforeNetwork(string amount)
    {
        var request = JsonNode.Parse(Sample("wallet-capital-create-request"))!; request["amount"] = amount;
        var handler = new Handler(Sample("wallet-capital"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(Create(Encoding.UTF8.GetBytes(request.ToJsonString())))).Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmicrosecondRequestAndUnknownPropertiesAreRejectedBeforeNetwork()
    {
        var handler = new Handler(Sample("wallet-capital"));
        var request = JsonNode.Parse(Sample("wallet-capital-create-request"))!;
        request["occurredAtUtc"] = "2026-10-14T08:00:00.0000001Z";
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(Create(Encoding.UTF8.GetBytes(request.ToJsonString())))).Should().ThrowAsync<ArgumentException>();
        request["occurredAtUtc"] = "2026-10-14T08:00:00Z"; request["balance"] = "999";
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(Create(Encoding.UTF8.GetBytes(request.ToJsonString())))).Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAndDetailPreserveFrozenCursorWithoutReconstructingBalance()
    {
        var handler = new Handler(Sample("wallet-capital-list"));
        var result = await Client(handler).SendV2Async(new(V2Operation.WalletCapitalList, Id: "wallet-synthetic", Limit: 200, Cursor: "signed/+cursor="));
        handler.Paths.Should().Equal("/api/v2/wallets/wallet-synthetic/capital-movements?limit=200&cursor=signed%2F%2Bcursor%3D");
        result.Body.GetProperty("snapshotCursor").GetString().Should().Be("synthetic-capital-snapshot");
        result.Body.TryGetProperty("balance", out _).Should().BeFalse();
        var detail = new Handler(Sample("wallet-capital"));
        await Client(detail).SendV2Async(new(V2Operation.WalletCapitalGet, Id: "wallet-synthetic", SubId: "capital-synthetic", Cursor: "snapshot/+"));
        detail.Paths.Should().Equal("/api/v2/wallets/wallet-synthetic/capital-movements/capital-synthetic?cursor=snapshot%2F%2B");
    }

    [Theory]
    [InlineData("walletId", "foreign")]
    [InlineData("source", "exchangeImported")]
    [InlineData("ledgerPolicyVersion", "other")]
    [InlineData("knownAtUtc", "2027-01-01T00:00:00Z")]
    [InlineData("amount", "126")]
    public async Task CreateRejectsForeignOrChangedEvidence(string field, string value)
    {
        var response = JsonNode.Parse(Sample("wallet-capital"))!; response["data"]![field] = value;
        await FluentActions.Awaiting(() => Client(new Handler(response.ToJsonString())).SendV2Async(Create())).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DetailRequiresExactIdentityAndStrongMatchingETag()
    {
        foreach (var etag in new string?[] { null, "W/\"wallet-capital-capital-synthetic\"", "\"other\"" })
            await FluentActions.Awaiting(() => Client(new Handler(Sample("wallet-capital")) { ETag = etag })
                .SendV2Async(new(V2Operation.WalletCapitalGet, Id: "wallet-synthetic", SubId: "capital-synthetic"))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(new Handler(Sample("wallet-capital"))).SendV2Async(new(V2Operation.WalletCapitalGet, Id: "wallet-synthetic", SubId: "other"))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListRejectsDuplicateIdentitiesAndFuturePublication()
    {
        var response = JsonNode.Parse(Sample("wallet-capital-list"))!;
        response["items"]!.AsArray().Add(response["items"]![0]!.DeepClone());
        await FluentActions.Awaiting(() => Client(new Handler(response.ToJsonString())).SendV2Async(new(V2Operation.WalletCapitalList, Id: "wallet-synthetic"))).Should().ThrowAsync<InvalidOperationException>();
        response["items"]!.AsArray().RemoveAt(1); response["items"]![0]!["publicationSequence"] = 3;
        await FluentActions.Awaiting(() => Client(new Handler(response.ToJsonString())).SendV2Async(new(V2Operation.WalletCapitalList, Id: "wallet-synthetic"))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task MissingAuthorityInvalidPageOrRequestSizeNeverSends()
    {
        var handler = new Handler(Sample("wallet-capital"));
        await FluentActions.Awaiting(() => Client(handler, session: null).SendV2Async(Create())).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(handler, workspace: null).SendV2Async(Create())).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.WalletCapitalList, Id: "wallet-synthetic", Limit: 201))).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(Create(new byte[65_537]))).Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task RefusalPreservesStatusWithoutPrintingUntrustedProblemSecrets()
    {
        var handler = new Handler("{\"detail\":\"credential-secret\"}") { Status = HttpStatusCode.Forbidden };
        var result = await FluentActions.Awaiting(() => Client(handler).SendV2Async(Create())).Should().ThrowAsync<HttpRequestException>();
        result.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        result.Which.Message.Should().NotContain("credential-secret");
    }

    [Fact]
    public async Task CommandsRegisterCreateListGetAndReturnServerEvidence()
    {
        var read = await Run(new Handler(Sample("wallet-capital")), ["wallet", "capital", "get", "wallet-synthetic", "capital-synthetic"]);
        read.Exit.Should().Be(0, read.Output);
        read.Output.Should().Contain("userDeclaredPaperCapital").And.Contain("125.25").And.Contain("snapshotCursor");
        var page = await Run(new Handler(Sample("wallet-capital-list")), ["wallet", "capital", "list", "wallet-synthetic", "--limit", "200"]);
        page.Exit.Should().Be(0, page.Output);
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, Sample("wallet-capital-create-request"));
            var write = await Run(new Handler(Sample("wallet-capital")), ["wallet", "capital", "create", "wallet-synthetic", "--request", file, "--idempotency-key", "command-key"]);
            write.Exit.Should().Be(0, write.Output);
            await File.WriteAllBytesAsync(file, new byte[65_537]);
            (await Run(new Handler("{}"), ["wallet", "capital", "create", "wallet-synthetic", "--request", file, "--idempotency-key", "command-key"])).Exit.Should().Be(2);
        }
        finally { File.Delete(file); }
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
        var services = new ServiceCollection(); services.AddSingleton<IApiClientFactory>(new Factory(handler));
        var prior = AnsiConsole.Console;
        try { AnsiConsole.Console = console; return (await Program.CreateCommandApp(new TypeRegistrar(services), console).RunAsync(args), output.ToString()); }
        finally { AnsiConsole.Console = prior; }
    }
}
