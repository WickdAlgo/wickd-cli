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

public sealed class PerformanceReportClientTests
{
    private sealed class Handler(params string[] bodies) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public byte[]? Request { get; private set; }
        public string? Key { get; private set; }
        public string? Match { get; private set; }
        public HttpStatusCode? Status { get; init; }
        public string? Etag { get; init; } = "\"setup-performance-performance-synthetic-1\"";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            request.Headers.GetValues("X-Wickd-Workspace").Single().Should().Be("workspace");
            request.Headers.GetValues("X-Wickd-Session").Single().Should().Be("session");
            Request = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token);
            request.Headers.Authorization!.ToString().Should().Be("Bearer service");
            Match = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            Key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            var response = new HttpResponseMessage(Status ?? (request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK))
            { Content = new StringContent(bodies[Math.Min(Paths.Count - 1, bodies.Length - 1)], Encoding.UTF8, "application/json") };
            if (Etag is not null) response.Headers.TryAddWithoutValidation("ETag", Etag);
            return response;
        }
    }
    private static WickdApiClient Client(Handler handler) => new(new HttpClient(handler), new WickdCliConfig
    { ApiUrl = "https://example.invalid", ApiToken = "service", SessionToken = "session", WorkspaceId = "workspace" });
    private static string Sample(string name, int version = 2)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "contracts", "samples", $"{name}.v{version}.sample.json"));
    }
    [Fact]
    public async Task CreateRetriesPreserveOriginalBytesKeyAndCompleteServerReceipt()
    {
        var bytes = Encoding.UTF8.GetBytes("\n" + Sample("setup-performance-query-request") + " \n");
        var handler = new Handler(Sample("setup-performance-report"));
        var call = new V2Call(V2Operation.PerformanceReportCreate, Request: bytes, IdempotencyKey: "stable-key");
        var response = await Client(handler).SendV2Async(call);
        await Client(handler).SendV2Async(call);
        handler.Request.Should().Equal(bytes);
        handler.Key.Should().Be("stable-key");
        handler.Paths.Should().Equal("/api/v2/performance/queries", "/api/v2/performance/queries");
        var report = response.Body.GetProperty("data").GetProperty("report");
        report.GetProperty("walletHorizons")[0].GetProperty("financialThroughExclusiveUtc").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        report.GetProperty("result").GetProperty("closedNetPnl").GetProperty("value").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        report.GetProperty("setups")[0].GetProperty("observation").GetProperty("originalRisk").GetProperty("initialRiskAmount").GetString().Should().Be("100");
        response.Body.GetProperty("snapshotCursor").GetString().Should().Be("synthetic-performance-selection-snapshot");
    }

    [Fact]
    public async Task CreateAcceptsServerNormalizationOfNullIdentitySelectors()
    {
        var query = JsonNode.Parse(Sample("setup-performance-query-request"))!;
        query["instrumentIds"] = null;
        var response = JsonNode.Parse(Sample("setup-performance-report"))!;
        response["data"]!["report"]!["query"]!["instrumentIds"] = new JsonArray();
        var bytes = Encoding.UTF8.GetBytes(query.ToJsonString());
        var handler = new Handler(response.ToJsonString());

        await Client(handler).SendV2Async(new(V2Operation.PerformanceReportCreate, Request: bytes, IdempotencyKey: "null-selector"));

        handler.Request.Should().Equal(bytes);
    }

    [Fact]
    public async Task RevisionForwardsOriginalReasonBytesAndStrongReceipt()
    {
        var report = JsonNode.Parse(Sample("setup-performance-report"))!;
        report["data"]!["revision"] = 2;
        report["data"]!["priorResultId"] = "prior";
        report["data"]!["recalculationReason"] = "Reconcile late funding.";
        var handler = new Handler(report.ToJsonString());
        var bytes = Encoding.UTF8.GetBytes("\n" + Sample("setup-performance-report-revision-request"));
        var call = new V2Call(V2Operation.PerformanceReportRevise, Id: "prior", Request: bytes, IdempotencyKey: "revision-key", IfMatch: "\"old-receipt\"");
        await Client(handler).SendV2Async(call);
        await Client(handler).SendV2Async(call);
        handler.Request.Should().Equal(bytes);
        handler.Key.Should().Be("revision-key");
        handler.Match.Should().Be("\"old-receipt\"");
        handler.Paths.Should().OnlyContain(x => x == "/api/v2/performance/results/prior/revisions");
    }

    [Fact]
    public async Task RefusesForeignResultQueryAndMismatchedReceipt()
    {
        await FluentActions.Awaiting(() => Client(new Handler(Sample("setup-performance-report"))).SendV2Async(
            new(V2Operation.PerformanceReportGet, Id: "foreign"))).Should().ThrowAsync<InvalidOperationException>();
        var changed = JsonNode.Parse(Sample("setup-performance-report"))!;
        changed["data"]!["report"]!["query"]!["walletIds"] = new JsonArray("foreign");
        await FluentActions.Awaiting(() => Client(new Handler(changed.ToJsonString())).SendV2Async(new(V2Operation.PerformanceReportCreate,
            Request: Encoding.UTF8.GetBytes(Sample("setup-performance-query-request")), IdempotencyKey: "key")))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => Client(new Handler(Sample("setup-performance-report")) { Etag = "\"other\"" }).SendV2Async(
            new(V2Operation.PerformanceReportGet, Id: "performance-synthetic"))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("W/\"weak\"")]
    [InlineData("\"one\", \"two\"")]
    public async Task RefusesInvalidRevisionReceiptBeforeSending(string? etag)
    {
        var handler = new Handler("{}");
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.PerformanceReportRevise, Id: "prior",
            Request: Encoding.UTF8.GetBytes(Sample("setup-performance-report-revision-request")), IdempotencyKey: "key", IfMatch: etag)))
            .Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task LocalInvalidTypesDuplicatePropertiesHeadersAndPathsNeverSend()
    {
        var handler = new Handler("{}");
        var json = Sample("setup-performance-query-request");
        foreach (var request in new[] { json.Replace("\"mode\":\"paper\"", "\"mode\":123"), json.TrimEnd().TrimEnd('}') + ",\"mode\":\"paper\"}" })
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.PerformanceReportCreate,
                Request: Encoding.UTF8.GetBytes(request), IdempotencyKey: "key"))).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.PerformanceReportGet, Id: ".."))).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.PerformanceReportCreate,
            Request: Encoding.UTF8.GetBytes(json), IdempotencyKey: "key\nsecret"))).Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task StaleRevisionExposesOnlySafeRecoveryMetadata()
    {
        var problem = "{\"currentResultId\":\"current\",\"currentRevision\":2,\"currentETag\":\"\\\"safe\\\"\",\"currentResultUrl\":\"/api/v2/performance/results/current\",\"detail\":\"secret-credential\"}";
        var handler = new Handler(problem) { Status = HttpStatusCode.PreconditionFailed };
        var call = new V2Call(V2Operation.PerformanceReportRevise, Id: "prior",
            Request: Encoding.UTF8.GetBytes(Sample("setup-performance-report-revision-request")),
            IdempotencyKey: "revision-key", IfMatch: "\"old-receipt\"");
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(call))
            .Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        error.Which.Message.Should().Contain("current").And.Contain("safe").And.NotContain("secret-credential");
        var unsafeProblem = problem.Replace("/api/v2/performance/results/current", "https://evil.invalid/secret-credential");
        var unsafeError = await FluentActions.Awaiting(() => Client(new Handler(unsafeProblem) { Status = HttpStatusCode.PreconditionFailed })
            .SendV2Async(call)).Should().ThrowAsync<HttpRequestException>();
        unsafeError.Which.Message.Should().NotContain("secret-credential").And.NotContain("evil.invalid");
    }

    [Fact]
    public async Task CommandsExposeServerStringsNullsHorizonsAndValidateFiles()
    {
        var read = await Run(new Handler(Sample("setup-performance-report")), ["performance", "report", "get", "performance-synthetic"]);
        read.Exit.Should().Be(0, read.Output);
        read.Output.Should().Contain("financialThroughExclusiveUtc").And.Contain("initialRiskAmount").And.Contain("snapshotCursor").And.Contain("null");
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, Sample("setup-performance-query-request"));
            var create = await Run(new Handler(Sample("setup-performance-report")), ["performance", "report", "create", "--request", file, "--idempotency-key", "key"]);
            create.Exit.Should().Be(0, create.Output);
            await File.WriteAllTextAsync(file, Sample("setup-performance-report-revision-request"));
            (await Run(new Handler("{}"), ["performance", "report", "revise", "prior", "--request", file, "--idempotency-key", "key", "--if-match", "*"])).Exit.Should().Be(2);
            await File.WriteAllBytesAsync(file, new byte[65_537]);
            (await Run(new Handler("{}"), ["performance", "report", "create", "--request", file, "--idempotency-key", "key"])).Exit.Should().Be(2);
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
