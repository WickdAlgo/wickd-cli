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

public sealed class StrategyExampleClientTests
{
    private sealed class Handler(params string[] bodies) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public byte[]? Request { get; private set; }
        public string? Key { get; private set; }
        public string? Etag { get; init; } = "\"example-strong\"";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            request.Headers.GetValues("X-Wickd-Workspace").Single().Should().Be("workspace");
            request.Headers.GetValues("X-Wickd-Session").Single().Should().Be("session");
            Request = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token);
            Key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            var response = new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
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
    public async Task CreateRetainsExactBytesKeyAndSubmittedReference()
    {
        var bytes = Encoding.UTF8.GetBytes("\n" + Sample("strategy-example-create-request") + " \n");
        var handler = new Handler(Sample("strategy-example-reference"));
        var call = new V2Call(V2Operation.StrategyExampleCreate, Id: "strategy-synthetic", Request: bytes, IdempotencyKey: "illustration-key");
        var response = await Client(handler).SendV2Async(call);
        await Client(handler).SendV2Async(call);
        response.Status.Should().Be(201);
        handler.Request.Should().Equal(bytes);
        handler.Key.Should().Be("illustration-key");
        handler.Paths.Should().Equal("/api/v2/strategies/strategy-synthetic/examples", "/api/v2/strategies/strategy-synthetic/examples");
    }
    [Theory]
    [InlineData("versionId", "foreign-version")]
    [InlineData("snapshotId", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("observationId", "foreign-observation")]
    [InlineData("annotation", "different annotation")]
    public async Task CreateRefusesAResponseForDifferentSubmittedEvidence(string property, string value)
    {
        var reference = JsonNode.Parse(Sample("strategy-example-reference"))!;
        reference["data"]![property] = value;
        await FluentActions.Awaiting(() => Client(new Handler(reference.ToJsonString())).SendV2Async(new(V2Operation.StrategyExampleCreate,
            Id: "strategy-synthetic", Request: Encoding.UTF8.GetBytes(Sample("strategy-example-create-request")), IdempotencyKey: "key")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*submitted reference*");
    }
    [Fact]
    public async Task ListDefaultsToDecisionOnlyPreservesOpaqueCursorAndAllowsEmptyPage()
    {
        var page = JsonNode.Parse(Sample("strategy-example-page"))!;
        page["data"]!["items"] = new JsonArray();
        var handler = new Handler(page.ToJsonString());
        var cursor = "signed+/=&?";
        await Client(handler).SendV2Async(new(V2Operation.StrategyExampleList, Id: "strategy/1", Cursor: cursor, Limit: 17));
        handler.Paths.Single().Should().Be("/api/v2/strategies/strategy%2F1/examples?limit=17&category=decisionOnly&cursor=" + Uri.EscapeDataString(cursor));
    }
    [Fact]
    public async Task OutcomeReferencesRequireExplicitCategoryAndExactIdentity()
    {
        var reference = JsonNode.Parse(Sample("strategy-example-reference"))!;
        reference["data"]!["category"] = "outcomeExample";
        var handler = new Handler(reference.ToJsonString());
        var call = new V2Call(V2Operation.StrategyExampleGet, Id: "strategy-synthetic", SubId: "example-synthetic", ExampleCategory: "outcomeExample");
        await Client(handler).SendV2Async(call);
        handler.Paths.Single().Should().EndWith("/example-synthetic?category=outcomeExample");
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(call with { ExampleCategory = null }))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*identity or category*");
    }
    [Fact]
    public async Task EvidenceValidatesExactRetainedSourceAgainstFrozenReference()
    {
        var reference = JsonNode.Parse(Sample("strategy-example-reference"))!;
        var evidence = JsonNode.Parse(Sample("setup-research-example", 1))!;
        reference["data"]!["observationId"] = evidence["observation"]!["observationId"]!.GetValue<string>();
        var handler = new Handler(reference.ToJsonString(), evidence.ToJsonString());
        var response = await Client(handler).SendV2Async(new(V2Operation.StrategyExampleEvidence,
            Id: "strategy-synthetic", SubId: "example-synthetic", Cursor: "receipt+/="));
        response.Body.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        handler.Paths.Should().Equal(
            "/api/v2/strategies/strategy-synthetic/examples/example-synthetic?category=decisionOnly&cursor=receipt%2B%2F%3D",
            "/api/v2/strategies/strategy-synthetic/examples/example-synthetic/decision-evidence?cursor=receipt%2B%2F%3D");
        evidence["observation"]!["observationId"] = new string('b', 64);
        await FluentActions.Awaiting(() => Client(new Handler(reference.ToJsonString(), evidence.ToJsonString())).SendV2Async(
            new(V2Operation.StrategyExampleEvidence, Id: "strategy-synthetic", SubId: "example-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact strategy-example source*");
    }
    [Theory]
    [InlineData("strategyId", "foreign-strategy")]
    [InlineData("exampleId", "foreign-example")]
    [InlineData("category", "outcomeExample")]
    public async Task DetailRefusesForeignIdentityOrUnexpectedCategory(string property, string value)
    {
        var reference = JsonNode.Parse(Sample("strategy-example-reference"))!;
        reference["data"]![property] = value;
        await FluentActions.Awaiting(() => Client(new Handler(reference.ToJsonString())).SendV2Async(
            new(V2Operation.StrategyExampleGet, Id: "strategy-synthetic", SubId: "example-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>();
    }
    [Fact]
    public async Task PageRefusesOutcomeOrForeignStrategyRows()
    {
        foreach (var property in new[] { "strategyId", "category" })
        {
            var page = JsonNode.Parse(Sample("strategy-example-page"))!;
            page["data"]!["items"]![0]![property] = property == "category" ? "outcomeExample" : "foreign-strategy";
            await FluentActions.Awaiting(() => Client(new Handler(page.ToJsonString())).SendV2Async(
                new(V2Operation.StrategyExampleList, Id: "strategy-synthetic")))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }
    [Theory]
    [InlineData(null)]
    [InlineData("W/\"weak\"")]
    public async Task ImmutableReferenceRequiresStrongResponseEtag(string? etag)
    {
        await FluentActions.Awaiting(() => Client(new Handler(Sample("strategy-example-reference")) { Etag = etag }).SendV2Async(
            new(V2Operation.StrategyExampleGet, Id: "strategy-synthetic", SubId: "example-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*strong ETag*");
    }
    [Fact]
    public async Task LocalBoundsRefuseBeforeSendingAndEvidenceRefusesOutcomeSelection()
    {
        var handler = new Handler("{}");
        foreach (var call in new[] {
            new V2Call(V2Operation.StrategyExampleList, Id: "s", Limit: 201),
            new V2Call(V2Operation.StrategyExampleList, Id: "s", Cursor: "bad\nreceipt"),
            new V2Call(V2Operation.StrategyExampleList, Id: "s", ExampleCategory: "automatic"),
            new V2Call(V2Operation.StrategyExampleEvidence, Id: "s", SubId: "e", ExampleCategory: "outcomeExample"),
            new V2Call(V2Operation.StrategyExampleCreate, Id: "s", Request: new byte[65_537], IdempotencyKey: "key") })
            await FluentActions.Awaiting(() => Client(handler).SendV2Async(call)).Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }
    [Fact]
    public async Task UnsupportedResponseVersionAndDuplicateInputAreRefused()
    {
        var reference = JsonNode.Parse(Sample("strategy-example-reference"))!;
        reference["schemaVersion"] = 3;
        await FluentActions.Awaiting(() => Client(new Handler(reference.ToJsonString())).SendV2Async(
            new(V2Operation.StrategyExampleGet, Id: "strategy-synthetic", SubId: "example-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>();
        var request = Sample("strategy-example-create-request").TrimEnd().TrimEnd('}') + ",\"annotation\":\"duplicate\"}";
        var handler = new Handler("{}");
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.StrategyExampleCreate, Id: "s",
            Request: Encoding.UTF8.GetBytes(request), IdempotencyKey: "key"))).Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task CommandsDispatchAllOperationsAndReturnValidationExitCodes()
    {
        var reference = JsonNode.Parse(Sample("strategy-example-reference"))!;
        var evidence = JsonNode.Parse(Sample("setup-research-example", 1))!;
        reference["data"]!["observationId"] = evidence["observation"]!["observationId"]!.GetValue<string>();
        (await Run(new Handler(Sample("strategy-example-page")), ["strategy", "example", "list", "strategy-synthetic"])).Exit.Should().Be(0);
        (await Run(new Handler(Sample("strategy-example-reference")), ["strategy", "example", "get", "strategy-synthetic", "example-synthetic"])).Exit.Should().Be(0);
        (await Run(new Handler(reference.ToJsonString(), evidence.ToJsonString()), ["strategy", "example", "evidence", "strategy-synthetic", "example-synthetic"])).Exit.Should().Be(0);
        var file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, Sample("strategy-example-create-request"));
            var created = await Run(new Handler(Sample("strategy-example-reference")), ["strategy", "example", "create", "strategy-synthetic", "--request", file, "--idempotency-key", "stable-key"]);
            created.Exit.Should().Be(0, created.Output);
            created.Output.Should().Contain("humanIllustrative");
            await File.WriteAllBytesAsync(file, new byte[65_537]);
            (await Run(new Handler("{}"), ["strategy", "example", "create", "strategy-synthetic", "--request", file, "--idempotency-key", "key"])).Exit.Should().Be(2);
        }
        finally { File.Delete(file); }
        (await Run(new Handler("{}"), ["strategy", "example", "list", "strategy-synthetic", "--limit", "201"])).Exit.Should().Be(2);
        (await Run(new Handler("{}"), ["strategy", "example", "evidence", "strategy-synthetic", "example-synthetic", "--category", "outcomeExample"])).Exit.Should().Be(2);
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
