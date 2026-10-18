using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Tests;

public sealed class SetupReviewClientTests
{
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK, string? etag = "\"setup-review-2\"") : HttpMessageHandler
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
            if (etag is not null) response.Headers.TryAddWithoutValidation("ETag", etag);
            return response;
        }
    }

    private static WickdApiClient Client(Handler handler) => new(new HttpClient(handler), new WickdCliConfig
    {
        ApiUrl = "https://example.invalid", SessionToken = "session", WorkspaceId = "workspace", ApiToken = "service"
    });

    private static string Sample(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "contracts", "samples", name + ".v2.sample.json"));
    }

    [Fact]
    public async Task AppendForwardsExactRequestBytesKeyAndIndependentReviewEtag()
    {
        var bytes = Encoding.UTF8.GetBytes("\n" + Sample("setup-review-request") + " \n");
        var revision = JsonNode.Parse(Sample("setup-review-revision"))!;
        revision["data"]!["setupId"] = "setup/1";
        var handler = new Handler(revision.ToJsonString(), HttpStatusCode.Created);
        var call = new V2Call(V2Operation.SetupReviewAppend, Id: "setup/1", Request: bytes,
            IdempotencyKey: "review-key", IfMatch: "\"setup-review-2\"");
        var result = await Client(handler).SendV2Async(call);
        var retry = await Client(handler).SendV2Async(call);

        result.Status.Should().Be(201);
        retry.ETag.Should().Be("\"setup-review-2\"");
        handler.Path.Should().Be("/api/v2/setups/setup%2F1/reviews");
        handler.Request.Should().Equal(bytes);
        handler.Key.Should().Be("review-key");
        handler.Match.Should().Be("\"setup-review-2\"");
        handler.Count.Should().Be(2);
    }

    [Fact]
    public async Task LatestNoReviewPreservesStrongStreamEtagAndCursor()
    {
        var response = JsonNode.Parse(Sample("setup-review-view"))!;
        response["data"]!["setupId"] = "setup-1";
        response["data"]!["status"] = "noReview";
        response["data"]!["review"] = null;
        var handler = new Handler(response.ToJsonString(), etag: "\"setup-review-0\"");
        var cursor = "signed-review-receipt+/=&?";
        var result = await Client(handler).SendV2Async(new(V2Operation.SetupReviewGet, Id: "setup-1", Cursor: cursor));

        result.ETag.Should().Be("\"setup-review-0\"");
        handler.Path.Should().Be("/api/v2/setups/setup-1/review?cursor=" + Uri.EscapeDataString(cursor));
        result.Body.GetProperty("data").GetProperty("status").GetString().Should().Be("noReview");
        result.Body.GetProperty("data").GetProperty("review").ValueKind.ToString().Should().Be("Null");
    }

    [Fact]
    public async Task HistoryUsesReviewCursorFamilyAndAllowsEmptyPages()
    {
        var page = JsonNode.Parse(Sample("setup-review-page"))!;
        page["data"]!["items"] = new JsonArray();
        var handler = new Handler(page.ToJsonString(), etag: null);
        var cursor = "opaque-review-page+/=";
        await Client(handler).SendV2Async(new(V2Operation.SetupReviewHistory, Id: "setup-1", Limit: 17, Cursor: cursor));
        handler.Path.Should().Be("/api/v2/setups/setup-1/reviews?limit=17&cursor=" + Uri.EscapeDataString(cursor));
    }

    [Fact]
    public async Task RevisionRequiresBothSetupAndReviewIdentity()
    {
        var valid = JsonNode.Parse(Sample("setup-review-revision"))!;
        var handler = new Handler(valid.ToJsonString());
        await Client(handler).SendV2Async(new(V2Operation.SetupReviewRevisionGet, Id: "setup-synthetic", SubId: "review-synthetic"));
        handler.Path.Should().Be("/api/v2/setups/setup-synthetic/reviews/review-synthetic");

        valid["data"]!["reviewId"] = "foreign-review";
        await FluentActions.Awaiting(() => Client(new Handler(valid.ToJsonString())).SendV2Async(
            new(V2Operation.SetupReviewRevisionGet, Id: "setup-synthetic", SubId: "review-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*identity*");
    }

    [Fact]
    public async Task LatestAndHistoryRejectForeignSetupIdentities()
    {
        var latest = JsonNode.Parse(Sample("setup-review-view"))!;
        latest["data"]!["setupId"] = "foreign-setup";
        await FluentActions.Awaiting(() => Client(new Handler(latest.ToJsonString())).SendV2Async(
            new(V2Operation.SetupReviewGet, Id: "setup-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*identity*");

        var page = JsonNode.Parse(Sample("setup-review-page"))!;
        page["data"]!["items"]![0]!["setupId"] = "foreign-setup";
        await FluentActions.Awaiting(() => Client(new Handler(page.ToJsonString())).SendV2Async(
            new(V2Operation.SetupReviewHistory, Id: "setup-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*different setup*");
    }

    [Fact]
    public async Task AppendRejectsWeakMissingOrInvalidStreamPreconditionsBeforeNetwork()
    {
        var bytes = Encoding.UTF8.GetBytes(Sample("setup-review-request"));
        var handler = new Handler(Sample("setup-review-revision"));
        var call = new V2Call(V2Operation.SetupReviewAppend, Id: "setup", Request: bytes,
            IdempotencyKey: "review-key", IfMatch: "\"setup-review-2\"");
        foreach (var invalid in new[] { "*", "W/\"setup-review-2\"", "setup-review-2", "\"a\", \"b\"" })
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(call with { IfMatch = invalid })).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(call with { IdempotencyKey = null })).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(call with { Request = new byte[65_537] })).Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }
}
