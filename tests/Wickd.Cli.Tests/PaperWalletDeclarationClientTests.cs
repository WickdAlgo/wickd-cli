using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Tests;

/// <summary>Binds raw zero-only declarations to their request and never presents them as a valuation.</summary>
public sealed class PaperWalletDeclarationClientTests
{
    private const string StreamETag = "\"paper-wallet-declaration-0123456789abcdef-2\"";

    private sealed class Handler(string body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<byte[]?> Requests { get; } = [];
        public List<string?> Keys { get; } = [];
        public List<string?> Preconditions { get; } = [];
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string? ETag { get; init; } = StreamETag;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            request.Headers.GetValues("X-Wickd-Workspace").Single().Should().Be("workspace");
            request.Headers.GetValues("X-Wickd-Session").Single().Should().Be("session");
            Requests.Add(request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token));
            Keys.Add(request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null);
            Preconditions.Add(request.Headers.TryGetValues("If-Match", out var matches) ? matches.Single() : null);
            var result = new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (ETag is not null) result.Headers.TryAddWithoutValidation("ETag", ETag);
            return result;
        }
    }

    private static WickdApiClient Client(Handler handler) => new(new HttpClient(handler), new WickdCliConfig
    { ApiUrl = "https://example.invalid", ApiToken = "service", SessionToken = "session", WorkspaceId = "workspace" });

    private static string Sample(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "contracts", "samples", name + ".v2.sample.json"));
    }

    private static V2Call Create(byte[]? bytes = null, string ifMatch = StreamETag) => new(V2Operation.WalletDeclarationCreate,
        Id: "wallet-synthetic", Request: bytes ?? Encoding.UTF8.GetBytes(Sample("paper-wallet-declaration-create-request")),
        IdempotencyKey: "stable-declaration", IfMatch: ifMatch);

    private static string Corrected()
    {
        var body = JsonNode.Parse(Sample("paper-wallet-declaration"))!;
        var declaration = body["data"]!["declaration"]!;
        declaration["declarationId"] = "declaration-corrected";
        declaration["rootDeclarationId"] = "declaration-synthetic";
        declaration["revision"] = 2;
        declaration["replacesDeclarationId"] = "declaration-synthetic";
        declaration["correctionReason"] = "Corrected assertions";
        return body.ToJsonString();
    }

    private static string Mutated(Action<JsonNode> change, string sample = "paper-wallet-declaration")
    {
        var body = JsonNode.Parse(Sample(sample))!;
        change(body);
        return body.ToJsonString();
    }

    [Fact]
    public async Task CreateSendsOriginalBytesKeyAndStrongStreamPreconditionOnRetry()
    {
        var bytes = Encoding.UTF8.GetBytes("\n" + Sample("paper-wallet-declaration-create-request") + " \n");
        var handler = new Handler(Sample("paper-wallet-declaration")) { Status = HttpStatusCode.Created };
        var result = await Client(handler).SendV2Async(Create(bytes));
        await Client(handler).SendV2Async(Create(bytes));
        handler.Requests.Should().OnlyContain(b => b!.SequenceEqual(bytes));
        handler.Keys.Should().Equal("stable-declaration", "stable-declaration");
        handler.Preconditions.Should().Equal(StreamETag, StreamETag);
        handler.Paths.Should().OnlyContain(p => p == "/api/v2/wallets/wallet-synthetic/completeness-declarations");
        result.ETag.Should().Be(StreamETag);
        result.Body.GetProperty("data").GetProperty("reconciliation").GetProperty("limitations").EnumerateArray()
            .Select(x => x.GetString()).Should().Contain("noNumericalValuation");
    }

    [Fact]
    public async Task CorrectionTargetsTheReplacedDeclarationAndBindsItsLineage()
    {
        var handler = new Handler(Corrected()) { Status = HttpStatusCode.Created };
        await Client(handler).SendV2Async(new V2Call(V2Operation.WalletDeclarationCorrect, Id: "wallet-synthetic", SubId: "declaration-synthetic",
            Request: Encoding.UTF8.GetBytes(Sample("paper-wallet-declaration-correction-request")), IdempotencyKey: "fix", IfMatch: StreamETag));
        handler.Paths.Single().Should().Be("/api/v2/wallets/wallet-synthetic/completeness-declarations/declaration-synthetic/corrections");
        var wrongTarget = new Handler(Corrected()) { Status = HttpStatusCode.Created };
        await FluentActions.Awaiting(() => Client(wrongTarget).SendV2Async(new V2Call(V2Operation.WalletDeclarationCorrect, Id: "wallet-synthetic",
            SubId: "declaration-other", Request: Encoding.UTF8.GetBytes(Sample("paper-wallet-declaration-correction-request")),
            IdempotencyKey: "fix", IfMatch: StreamETag))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("W/\"paper-wallet-declaration-0123456789abcdef-2\"")]
    [InlineData("paper-wallet-declaration-unquoted")]
    public async Task WritesRequireAStrongQuotedStreamETagBeforeNetwork(string ifMatch)
    {
        var handler = new Handler(Sample("paper-wallet-declaration"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(Create(ifMatch: ifMatch))).Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    public static TheoryData<string> InvalidRequests => new()
    {
        Mutated(x => x["knownAtUtc"] = "2026-10-14T09:00:00+00:00", "paper-wallet-declaration-create-request"),
        Mutated(x => x["claims"]!.AsObject().Remove("openingPositionsKnownFlat"), "paper-wallet-declaration-create-request"),
        Mutated(x => x["claims"]!["openingPositionsKnownFlat"] = "yes", "paper-wallet-declaration-create-request"),
        Mutated(x => x["reason"] = "   ", "paper-wallet-declaration-create-request"),
        Mutated(x => x["throughOccurredAtUtc"] = "2026-10-14T08:00:00.0000001+00:00", "paper-wallet-declaration-create-request"),
    };

    /// <summary>Client authority, missing or non-boolean claims, blank reasons and lossy instants never leave the CLI.</summary>
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidDeclarationRequestsAreRejectedBeforeNetwork(string request)
    {
        var handler = new Handler(Sample("paper-wallet-declaration"));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(Create(Encoding.UTF8.GetBytes(request)))).Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    public static TheoryData<string> InconsistentResponses => new()
    {
        Mutated(x => x["data"]!["declaration"]!["claims"]!["liabilitiesKnownZeroThroughout"] = true),
        Mutated(x => x["data"]!["declaration"]!["claims"]!["openingPositionsKnownFlat"] = false),
        Mutated(x => x["data"]!["declaration"]!["walletId"] = "wallet-other"),
        Mutated(x => x["data"]!["reconciliation"]!["limitations"] = new JsonArray("userDeclarationOnly", "noIndependentCompletenessProof")),
        Mutated(x => x["data"]!["declaration"]!["inputWatermark"] = 0),
        Mutated(x => x["data"]!["declaration"]!["publicationSequence"] = 3),
        Mutated(x => x["data"]!["declaration"]!["reason"] = "Different reason"),
        Mutated(x => x["data"]!["declaration"]!["revision"] = 2),
        Mutated(x => x["data"]!["declaration"]!["source"] = "verifiedExchange"),
        Mutated(x => x["data"]!["declaration"]!["throughOccurredAtUtc"] = "2026-10-14T09:30:00+00:00"),
    };

    /// <summary>A served declaration must restate the submitted assertions and its explicit non-valuation limits.</summary>
    [Theory]
    [MemberData(nameof(InconsistentResponses))]
    public async Task ResponsesThatDoNotMatchTheSubmissionOrOverclaimAreRejected(string body)
    {
        await FluentActions.Awaiting(() => Client(new Handler(body) { Status = HttpStatusCode.Created }).SendV2Async(Create()))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WeakOrForeignResponseETagsAreRejected()
    {
        foreach (var etag in new string?[] { null, "W/" + StreamETag, "\"wallet-capital-x\"" })
            await FluentActions.Awaiting(() => Client(new Handler(Sample("paper-wallet-declaration")) { ETag = etag })
                .SendV2Async(new V2Call(V2Operation.WalletDeclarationGet, Id: "wallet-synthetic", SubId: "declaration-synthetic")))
                .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListAndGetBindLimitCursorWalletAndIdentity()
    {
        var handler = new Handler(Sample("paper-wallet-declaration-list"));
        await Client(handler).SendV2Async(new V2Call(V2Operation.WalletDeclarationList, Id: "wallet-synthetic", Limit: 10, Cursor: "c/1"));
        handler.Paths.Single().Should().Be("/api/v2/wallets/wallet-synthetic/completeness-declarations?limit=10&cursor=c%2F1");
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new V2Call(V2Operation.WalletDeclarationList, Id: "wallet-synthetic", Limit: 11)))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Client(new Handler(Sample("paper-wallet-declaration-list")))
            .SendV2Async(new V2Call(V2Operation.WalletDeclarationList, Id: "wallet-other", Limit: 10))).Should().ThrowAsync<InvalidOperationException>();
        var get = new Handler(Sample("paper-wallet-declaration"));
        await Client(get).SendV2Async(new V2Call(V2Operation.WalletDeclarationGet, Id: "wallet-synthetic", SubId: "declaration-synthetic", Cursor: "frozen"));
        get.Paths.Single().Should().Be("/api/v2/wallets/wallet-synthetic/completeness-declarations/declaration-synthetic?cursor=frozen");
        await FluentActions.Awaiting(() => Client(new Handler(Sample("paper-wallet-declaration")))
            .SendV2Async(new V2Call(V2Operation.WalletDeclarationGet, Id: "wallet-synthetic", SubId: "declaration-other")))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ListOrderingAndCursorRegressionAreRejected()
    {
        var duplicate = Mutated(x =>
        {
            var items = x["items"]!.AsArray();
            items.Add(items[0]!.DeepClone());
        }, "paper-wallet-declaration-list");
        await FluentActions.Awaiting(() => Client(new Handler(duplicate))
            .SendV2Async(new V2Call(V2Operation.WalletDeclarationList, Id: "wallet-synthetic", Limit: 10))).Should().ThrowAsync<InvalidOperationException>();
        var stalled = Mutated(x => x["nextCursor"] = "same", "paper-wallet-declaration-list");
        await FluentActions.Awaiting(() => Client(new Handler(stalled))
            .SendV2Async(new V2Call(V2Operation.WalletDeclarationList, Id: "wallet-synthetic", Limit: 10, Cursor: "same"))).Should().ThrowAsync<InvalidOperationException>();
    }
}
