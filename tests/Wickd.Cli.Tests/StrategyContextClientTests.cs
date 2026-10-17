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

public sealed class StrategyContextClientTests
{
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Method { get; private set; }
        public string? Path { get; private set; }
        public byte[]? Request { get; private set; }
        public string? Key { get; private set; }
        public string? Match { get; private set; }
        public int Count { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;
            Method = request.Method.Method;
            Path = request.RequestUri!.PathAndQuery;
            Request = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(token);
            Key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
            Match = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"context-2\"");
            return response;
        }
    }

    private static WickdApiClient Client(Handler handler, string? session = "session-synthetic", string? workspace = "workspace-synthetic") =>
        new(new HttpClient(handler), new WickdCliConfig
        {
            ApiUrl = "https://example.invalid", SessionToken = session, WorkspaceId = workspace, ApiToken = "service-synthetic"
        });

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }

    private static JsonNode Sample(string name) => JsonNode.Parse(File.ReadAllBytes(
        Path.Combine(Root(), "contracts", "samples", name + ".v2.sample.json")))!;

    private static string Text(JsonNode node) => node.ToJsonString();

    [Fact]
    public async Task SessionAndConditionCatalogPagesRetainFrozenPagination()
    {
        var sessions = new Handler(Text(Sample("session-definition-page")));
        await Client(sessions).SendV2Async(new(V2Operation.SessionDefinitionVersionList, Limit: 7, Cursor: "s+/=&?"));
        sessions.Path.Should().Be("/api/v2/session-definition-versions?limit=7&cursor=s%2B%2F%3D%26%3F");

        var conditions = new Handler(Text(Sample("market-condition-page")));
        await Client(conditions).SendV2Async(new(V2Operation.MarketConditionRevisionList, Limit: 4, Cursor: "c+/=&?"));
        conditions.Path.Should().Be("/api/v2/market-condition-revisions?limit=4&cursor=c%2B%2F%3D%26%3F");
    }

    [Fact]
    public async Task CatalogRevisionWritePreservesBytesKeyEtagAndStableIdentity()
    {
        var response = Sample("market-condition-revision");
        response["data"]!["tagId"] = "condition/id";
        var handler = new Handler(Text(response), HttpStatusCode.Created);
        var request = Encoding.UTF8.GetBytes(" \n" + Text(Sample("market-condition-request")) + "\n");
        var result = await Client(handler).SendV2Async(new(V2Operation.MarketConditionRevisionCreate,
            Id: "condition/id", Request: request, IdempotencyKey: "condition-revision-key", IfMatch: "\"condition-4\""));

        handler.Method.Should().Be("POST");
        handler.Path.Should().Be("/api/v2/market-condition-tags/condition%2Fid/revisions");
        handler.Request.Should().Equal(request);
        handler.Key.Should().Be("condition-revision-key");
        handler.Match.Should().Be("\"condition-4\"");
        result.ETag.Should().Be("\"context-2\"");
    }

    [Fact]
    public async Task BothStrategyScopesUseTheirCanonicalContractsAndSharedDraftEtag()
    {
        var instrumentScope = Sample("strategy-version-scope");
        var handler = new Handler(Text(instrumentScope));
        await Client(handler).SendV2Async(new(V2Operation.StrategyScopeGet, Id: "version-synthetic", Cursor: "frozen"));
        handler.Path.Should().Be("/api/v2/strategy-versions/version-synthetic/scope?cursor=frozen");

        var contextualScope = Sample("strategy-context-scope");
        handler = new Handler(Text(contextualScope));
        var request = Encoding.UTF8.GetBytes(Text(Sample("strategy-context-scope-request")));
        await Client(handler).SendV2Async(new(V2Operation.StrategyContextScopeUpdate,
            Id: "strategy-version-synthetic", Request: request, IdempotencyKey: "scope-key", IfMatch: "\"draft-3\""));
        handler.Method.Should().Be("PATCH");
        handler.Path.Should().Be("/api/v2/strategy-versions/strategy-version-synthetic/context-scope");
        handler.Request.Should().Equal(request);
        handler.Key.Should().Be("scope-key");
        handler.Match.Should().Be("\"draft-3\"");
    }

    [Fact]
    public async Task SetupContextReadBindsFrozenCursorAndBothFilters()
    {
        var view = Sample("setup-context-view");
        view["data"]!["setupId"] = "setup/id";
        view["data"]!["observation"]!["setupId"] = "setup/id";
        var handler = new Handler(Text(view));
        await Client(handler).SendV2Async(new(V2Operation.SetupContextGet, Id: "setup/id", Cursor: "snap+/=&?",
            SessionDefinitionVersionId: "session/version", MarketConditionTagId: "condition/tag"));
        handler.Path.Should().Be("/api/v2/setups/setup%2Fid/context?cursor=snap%2B%2F%3D%26%3F&sessionDefinitionVersionId=session%2Fversion&marketConditionTagId=condition%2Ftag");
    }

    [Fact]
    public async Task SetupListAndDetailRepeatSessionAndStableConditionFilters()
    {
        var page = new Handler(Text(Sample("journal-setup-list")));
        await Client(page).SendV2Async(new(V2Operation.SetupList, Limit: 20, Cursor: "next", SessionDefinitionVersionId: "session/v2", MarketConditionTagId: "range/tag"));
        page.Path.Should().Be("/api/v2/setups?limit=20&cursor=next&sessionDefinitionVersionId=session%2Fv2&marketConditionTagId=range%2Ftag");

        var detail = Sample("journal-setup");
        detail["data"]!["setupId"] = "setup/1";
        var read = new Handler(Text(detail));
        await Client(read).SendV2Async(new(V2Operation.SetupGet, Id: "setup/1", Cursor: "snapshot", SessionDefinitionVersionId: "session/v2", MarketConditionTagId: "range/tag"));
        read.Path.Should().Be("/api/v2/setups/setup%2F1?cursor=snapshot&sessionDefinitionVersionId=session%2Fv2&marketConditionTagId=range%2Ftag");
    }

    [Fact]
    public async Task SetupFiltersAreBoundFromListDetailAndContextCommands()
    {
        var page = new Handler(Text(Sample("journal-setup-list")));
        var listed = await Run(page, ["setup", "list", "--session-version", "session/v2", "--condition-tag", "range/tag"]);
        listed.Exit.Should().Be(0, listed.Output);
        page.Path.Should().Be("/api/v2/setups?limit=50&sessionDefinitionVersionId=session%2Fv2&marketConditionTagId=range%2Ftag");

        var detail = Sample("journal-setup");
        detail["data"]!["setupId"] = "setup/1";
        var read = new Handler(Text(detail));
        var got = await Run(read, ["setup", "get", "setup/1", "--session-version", "session/v2", "--condition-tag", "range/tag"]);
        got.Exit.Should().Be(0, got.Output);
        read.Path.Should().Be("/api/v2/setups/setup%2F1?sessionDefinitionVersionId=session%2Fv2&marketConditionTagId=range%2Ftag");

        var view = Sample("setup-context-view");
        view["data"]!["setupId"] = "setup/1";
        view["data"]!["observation"]!["setupId"] = "setup/1";
        var context = new Handler(Text(view));
        var contextual = await Run(context, ["setup", "context", "get", "setup/1", "--session-version", "session/v2", "--condition-tag", "range/tag"]);
        contextual.Exit.Should().Be(0, contextual.Output);
        context.Path.Should().Be("/api/v2/setups/setup%2F1/context?sessionDefinitionVersionId=session%2Fv2&marketConditionTagId=range%2Ftag");
    }

    [Fact]
    public async Task ContextPagesAllowTheApiMaximumOf200()
    {
        var handler = new Handler(Text(Sample("session-definition-page")));
        await Client(handler).SendV2Async(new(V2Operation.SessionDefinitionVersionList, Limit: 200));
        handler.Path.Should().Be("/api/v2/session-definition-versions?limit=200");

        await FluentActions.Awaiting(() => Client(handler).SendV2Async(
            new(V2Operation.SessionDefinitionVersionList, Limit: 201))).Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(1);
    }

    [Fact]
    public async Task SetupObservationAppendUsesIndependentEtagAndExactRequest()
    {
        var observation = Sample("setup-context-observation");
        observation["data"]!["setupId"] = "setup-context-1";
        var handler = new Handler(Text(observation), HttpStatusCode.Created);
        var request = Encoding.UTF8.GetBytes("\n" + Text(Sample("setup-context-request")) + " \n");
        await Client(handler).SendV2Async(new(V2Operation.SetupContextObservationCreate, Id: "setup-context-1", Request: request,
            IdempotencyKey: "observation-key", IfMatch: "\"setup-context-1\""));
        handler.Path.Should().Be("/api/v2/setups/setup-context-1/context-observations");
        handler.Request.Should().Equal(request);
        handler.Key.Should().Be("observation-key");
        handler.Match.Should().Be("\"setup-context-1\"");
    }

    [Fact]
    public async Task EveryStrategyContextOperationAcceptsItsCanonicalResponseShape()
    {
        byte[] Request(string name) => Encoding.UTF8.GetBytes(Text(Sample(name)));
        var calls = new (V2Operation Operation, string Response, V2Call Call)[]
        {
            (V2Operation.SessionDefinitionCreate, "session-definition-version", new(V2Operation.SessionDefinitionCreate,
                Request: Request("session-definition-request"), IdempotencyKey: "session-create")),
            (V2Operation.SessionDefinitionVersionCreate, "session-definition-version", new(V2Operation.SessionDefinitionVersionCreate,
                Id: "session-synthetic", Request: Request("session-definition-request"), IdempotencyKey: "session-revise", IfMatch: "\"session-1\"")),
            (V2Operation.SessionDefinitionVersionList, "session-definition-page", new(V2Operation.SessionDefinitionVersionList)),
            (V2Operation.SessionDefinitionVersionGet, "session-definition-version", new(V2Operation.SessionDefinitionVersionGet, Id: "session-version-synthetic")),
            (V2Operation.MarketConditionCreate, "market-condition-revision", new(V2Operation.MarketConditionCreate,
                Request: Request("market-condition-request"), IdempotencyKey: "condition-create")),
            (V2Operation.MarketConditionRevisionCreate, "market-condition-revision", new(V2Operation.MarketConditionRevisionCreate,
                Id: "condition-synthetic", Request: Request("market-condition-request"), IdempotencyKey: "condition-revise", IfMatch: "\"condition-1\"")),
            (V2Operation.MarketConditionRevisionList, "market-condition-page", new(V2Operation.MarketConditionRevisionList)),
            (V2Operation.MarketConditionRevisionGet, "market-condition-revision", new(V2Operation.MarketConditionRevisionGet, Id: "condition-revision-synthetic")),
            (V2Operation.StrategyScopeGet, "strategy-version-scope", new(V2Operation.StrategyScopeGet, Id: "version-synthetic")),
            (V2Operation.StrategyScopeUpdate, "strategy-version-scope", new(V2Operation.StrategyScopeUpdate, Id: "version-synthetic",
                Request: Request("strategy-scope-update-request"), IdempotencyKey: "scope-update", IfMatch: "\"draft-1\"")),
            (V2Operation.StrategyScopeList, "strategy-scope-list-page", new(V2Operation.StrategyScopeList, Id: "strategy-synthetic")),
            (V2Operation.StrategyContextScopeGet, "strategy-context-scope", new(V2Operation.StrategyContextScopeGet, Id: "strategy-version-synthetic")),
            (V2Operation.StrategyContextScopeUpdate, "strategy-context-scope", new(V2Operation.StrategyContextScopeUpdate,
                Id: "strategy-version-synthetic", Request: Request("strategy-context-scope-request"), IdempotencyKey: "context-scope-update", IfMatch: "\"draft-1\"")),
            (V2Operation.SetupContextObservationCreate, "setup-context-observation", new(V2Operation.SetupContextObservationCreate,
                Id: "setup-synthetic", Request: Request("setup-context-request"), IdempotencyKey: "context-append", IfMatch: "\"context-1\"")),
            (V2Operation.SetupContextObservationList, "setup-context-observation-page", new(V2Operation.SetupContextObservationList, Id: "setup-synthetic")),
            (V2Operation.SetupContextGet, "setup-context-view", new(V2Operation.SetupContextGet, Id: "setup-synthetic"))
        };

        foreach (var entry in calls)
        {
            var handler = new Handler(Text(Sample(entry.Response)));
            var response = await Client(handler).SendV2Async(entry.Call);
            response.Body.GetProperty("contract").GetString().Should().Be(entry.Response, entry.Operation.ToString());
            handler.Count.Should().Be(1, entry.Operation.ToString());
        }
    }

    [Fact]
    public async Task SetupContextHistoryAcceptsEmptyPageAndRejectsForeignItems()
    {
        var emptyPage = Sample("setup-context-observation-page");
        emptyPage["data"]!["items"] = new JsonArray();
        var emptyResponse = await Client(new Handler(Text(emptyPage))).SendV2Async(
            new(V2Operation.SetupContextObservationList, Id: "setup-synthetic"));
        emptyResponse.Body.GetProperty("data").GetProperty("items").GetArrayLength().Should().Be(0);

        var foreignPage = Sample("setup-context-observation-page");
        foreignPage["data"]!["items"]![0]!["setupId"] = "other-setup";
        await FluentActions.Awaiting(() => Client(new Handler(Text(foreignPage))).SendV2Async(
            new(V2Operation.SetupContextObservationList, Id: "setup-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Setup-context history contains a different setup identity.");
    }

    [Fact]
    public async Task SetupContextReadRejectsNestedObservationFromAnotherSetup()
    {
        var view = Sample("setup-context-view");
        view["data"]!["observation"]!["setupId"] = "other-setup";
        await FluentActions.Awaiting(() => Client(new Handler(Text(view))).SendV2Async(
            new(V2Operation.SetupContextGet, Id: "setup-synthetic")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Setup-context observation identity does not match the requested setup.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("W/\"weak\"")]
    [InlineData("unquoted")]
    public async Task ContextWritesRefuseMissingOrWeakEtagBeforeNetwork(string? etag)
    {
        var handler = new Handler(Text(Sample("setup-context-observation")));
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.SetupContextObservationCreate,
                Id: "setup-synthetic", Request: File.ReadAllBytes(Path.Combine(Root(), "contracts", "samples", "setup-context-request.v2.sample.json")),
                IdempotencyKey: "key", IfMatch: etag)))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task ContextWritesEnforceSchemaAndStrict64KiBLimitBeforeNetwork()
    {
        var handler = new Handler(Text(Sample("market-condition-revision")));
        var oversized = new byte[65_537];
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.MarketConditionCreate,
                Request: oversized, IdempotencyKey: "key")))
            .Should().ThrowAsync<ArgumentException>();
        var malformed = Encoding.UTF8.GetBytes("{\"label\":\"x\",\"archived\":true}");
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.SessionDefinitionCreate,
                Request: malformed, IdempotencyKey: "key")))
            .Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(V2Operation.SessionDefinitionVersionGet, "session-definition-version", "versionId", "foreign")]
    [InlineData(V2Operation.MarketConditionRevisionGet, "market-condition-revision", "revisionId", "foreign")]
    [InlineData(V2Operation.SetupContextGet, "setup-context-view", "setupId", "foreign")]
    public async Task ReadsRefuseForeignResourceIdentity(V2Operation operation, string sample, string field, string foreign)
    {
        var data = Sample(sample);
        data["data"]![field] = foreign;
        var handler = new Handler(Text(data));
        var call = operation switch
        {
            V2Operation.SessionDefinitionVersionGet => new V2Call(operation, Id: "version-synthetic"),
            V2Operation.MarketConditionRevisionGet => new V2Call(operation, Id: "revision-synthetic"),
            _ => new V2Call(operation, Id: "setup-synthetic")
        };
        await FluentActions.Awaiting(() => Client(handler).SendV2Async(call)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpstreamErrorBodyIsNotEchoed()
    {
        var handler = new Handler("private database path and token", HttpStatusCode.Forbidden);
        var error = await FluentActions.Awaiting(() => Client(handler).SendV2Async(new(V2Operation.SessionDefinitionVersionList)))
            .Should().ThrowAsync<HttpRequestException>();
        error.Which.Message.Should().NotContain("private database").And.NotContain("token");
        error.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
