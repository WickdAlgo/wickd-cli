using System.Net;
using System.Security.Cryptography;
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

public sealed class MarketHistoryClientTests
{
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Path { get; private set; }
        public HttpMethod? Method { get; private set; }
        public bool HasRequestBody { get; private set; }
        public Dictionary<string, string> Headers { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Count++;Path=request.RequestUri!.PathAndQuery;Method=request.Method;HasRequestBody=request.Content is not null;
            foreach(var header in request.Headers)Headers[header.Key]=string.Join(",",header.Value);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content=new StringContent(body,Encoding.UTF8,"application/json"),
                ReasonPhrase="upstream internal /srv/private/storage redacted-session-secret"
            });
        }
    }

    private static WickdApiClient Client(Handler handler, string? session="redacted-session-secret", string? workspace="workspace-synthetic") =>
        new(new HttpClient(handler),new WickdCliConfig{ApiUrl="https://example.invalid",ApiToken="redacted-host-secret",SessionToken=session,WorkspaceId=workspace});
    private static string Root()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"Wickd.Cli.slnx")))directory=directory.Parent;
        return directory!.FullName;
    }
    private static string Sample(string contract)=>File.ReadAllText(Path.Combine(Root(),"contracts","samples",contract+".v2.sample.json"));
    public static TheoryData<V2Operation,string,string,string[]> Reads=>new()
    {
        {V2Operation.MarketSnapshotGet,"market-history","/api/v2/market-history/snapshots/series%2Fa%3Fb%26c",["history","snapshot","get"]},
        {V2Operation.MarketSeriesCoverage,"market-coverage","/api/v2/market-history/series/series%2Fa%3Fb%26c/coverage",["history","series","coverage"]}
    };

    [Theory,MemberData(nameof(Reads))]
    public async Task CommandsReadExactGeneratedEvidenceWithSessionAndWorkspace(V2Operation operation,string contract,string path,string[] command)
    {
        var handler=new Handler(Sample(contract));
        var result=await Run(handler,command.Concat(["series/a?b&c"]).ToArray());
        result.Exit.Should().Be(0);handler.Count.Should().Be(1);handler.Path.Should().Be(path);
        handler.Method.Should().Be(HttpMethod.Get);handler.HasRequestBody.Should().BeFalse();
        handler.Headers["X-Wickd-Session"].Should().Be("redacted-session-secret");
        handler.Headers["X-Wickd-Workspace"].Should().Be("workspace-synthetic");
        handler.Headers["Authorization"].Should().Be("Bearer redacted-host-secret");
        using var json=JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("status").GetInt32().Should().Be(200);
        json.RootElement.GetProperty("body").GetProperty("contract").GetString().Should().Be(contract);
        result.Output.Should().NotContain("redacted-session-secret").And.NotContain("redacted-host-secret").And.NotContain("/srv/private");
        var response=await Client(new Handler(Sample(contract))).SendV2Async(new V2Call(operation,Id:"series/a?b&c"));
        response.Body.GetRawText().Should().Be(Sample(contract).Trim());
    }

    [Theory]
    [InlineData(V2Operation.MarketSnapshotGet,"market-history")]
    [InlineData(V2Operation.MarketSeriesCoverage,"market-coverage")]
    public async Task BothReadsRefuseMissingCredentialsBeforeNetwork(V2Operation operation,string contract)
    {
        var handler=new Handler(Sample(contract));
        await FluentActions.Awaiting(()=>Client(handler,session:null).SendV2Async(new V2Call(operation,Id:"identity"))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(()=>Client(handler,workspace:null).SendV2Async(new V2Call(operation,Id:"identity"))).Should().ThrowAsync<InvalidOperationException>();
        handler.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("")][InlineData(".")][InlineData("..")][InlineData(" leading")][InlineData("trailing ")][InlineData("id\n")]
    public async Task InvalidIdentityIsRefusedWithoutNetwork(string identity)
    {
        var handler=new Handler(Sample("market-history"));
        await FluentActions.Awaiting(()=>Client(handler).SendV2Async(new V2Call(V2Operation.MarketSnapshotGet,Id:identity))).Should().ThrowAsync<ArgumentException>();
        handler.Count.Should().Be(0);
    }

    [Fact]
    public async Task SnapshotCommandPreservesLongDecimalStringsAndExplicitMissingBootstrap()
    {
        var sample=JsonNode.Parse(Sample("market-history"))!;
        var precise="1."+new string('0',180)+"1";
        sample["data"]!["candles"]![0]!["open"]=precise;
        var result=await Run(new Handler(sample.ToJsonString()),["history","snapshot","get","snapshot"]);
        result.Exit.Should().Be(0);
        using var output=JsonDocument.Parse(result.Output);
        var body=output.RootElement.GetProperty("body").GetProperty("data");
        body.GetProperty("candles")[0].GetProperty("open").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("candles")[0].GetProperty("open").GetString().Should().Be(precise);
        body.GetProperty("snapshot").GetProperty("bootstrap").GetString().Should().Be("unknown");
        body.GetProperty("snapshot").GetProperty("coverage").GetString().Should().Be("unknown");
        body.GetProperty("series").GetProperty("normalizationVersion").GetString().Should().Be("v1");
    }

    [Theory]
    [InlineData("numeric")][InlineData("newline")][InlineData("exponent")][InlineData("missing")][InlineData("version")][InlineData("contract")][InlineData("duplicate")]
    public async Task SnapshotRejectsMalformedContractsInsteadOfDisplayingFallbackEvidence(string corruption)
    {
        var sample=JsonNode.Parse(Sample("market-history"))!;
        switch(corruption)
        {
            case "numeric":sample["data"]!["candles"]![0]!["open"]=1;break;
            case "newline":sample["data"]!["candles"]![0]!["open"]="1\n";break;
            case "exponent":sample["data"]!["candles"]![0]!["open"]="1e2";break;
            case "missing":sample["data"]!.AsObject().Remove("series");break;
            case "version":sample["schemaVersion"]=1;break;
            case "contract":sample["contract"]="market-coverage";break;
        }
        var body=sample.ToJsonString();
        if(corruption=="duplicate")body="{\"schemaVersion\":2,"+body[1..];
        var result=await Run(new Handler(body),["history","snapshot","get","snapshot"]);
        result.Exit.Should().Be(1);result.Output.Should().Contain("Unsupported or malformed API response");
        result.Output.Should().NotContain("candles").And.NotContain("redacted-session-secret");
    }

    [Fact]
    public async Task CoverageAbsenceRemainsEmptyAndUnavailableLimitationsAreRendered()
    {
        var sample=JsonNode.Parse(Sample("market-coverage"))!;
        sample["items"]=new JsonArray();
        var empty=await Run(new Handler(sample.ToJsonString()),["history","series","coverage","series"]);
        empty.Exit.Should().Be(0);
        using(var output=JsonDocument.Parse(empty.Output))output.RootElement.GetProperty("body").GetProperty("items").GetArrayLength().Should().Be(0);
        sample=JsonNode.Parse(Sample("market-coverage"))!;
        sample["items"]![0]!["state"]="unavailable";
        sample["items"]![0]!["limitation"]="Provider history before listing is unavailable.";
        var unavailable=await Run(new Handler(sample.ToJsonString()),["history","series","coverage","series"]);
        unavailable.Exit.Should().Be(0);unavailable.Output.Should().Contain("unavailable").And.Contain("before listing");
    }

    [Fact]
    public async Task CoverageRejectsMissingCheckedObservationFields()
    {
        var sample=JsonNode.Parse(Sample("market-coverage"))!;
        sample["items"]![0]!.AsObject().Remove("checkedAtUtc");
        var result=await Run(new Handler(sample.ToJsonString()),["history","series","coverage","series"]);
        result.Exit.Should().Be(1);result.Output.Should().Contain("Unsupported or malformed API response");
    }

    [Theory]
    [InlineData(401)][InlineData(403)][InlineData(404)][InlineData(503)]
    public async Task RefusalsPreserveStatusAndRedactArbitraryStorageAndSecretDetails(int status)
    {
        const string unsafeBody="{\"code\":\"retained_evidence_unavailable\",\"detail\":\"/srv/private/storage redacted-session-secret redacted-host-secret\"}";
        var handler=new Handler(unsafeBody,(HttpStatusCode)status);
        var exception=await FluentActions.Awaiting(()=>Client(handler).SendV2Async(new V2Call(V2Operation.MarketSnapshotGet,Id:"snapshot"))).Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be((HttpStatusCode)status);
        exception.Which.Message.Should().Contain(status.ToString()).And.NotContain("/srv/private").And.NotContain("redacted-");
        var result=await Run(new Handler(unsafeBody,(HttpStatusCode)status),["history","snapshot","get","snapshot"]);
        result.Exit.Should().Be(1);result.Output.Should().Contain(status.ToString()).And.NotContain("/srv/private").And.NotContain("redacted-");
        if(status==503)result.Output.Should().Contain("Retained evidence is unavailable");
        if(status==404)result.Output.Should().Contain("source entitlement");
    }

    [Fact]
    public async Task CoverageRefusalUsesTheSameSafeStatusPathAndHelpHasNoAcquisitionOptions()
    {
        var exception=await FluentActions.Awaiting(()=>Client(new Handler("/private/path secret",HttpStatusCode.ServiceUnavailable))
            .SendV2Async(new V2Call(V2Operation.MarketSeriesCoverage,Id:"series"))).Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        exception.Which.Message.Should().NotContain("/private/path").And.NotContain("secret");
        var help=await Run(new Handler("{}"),["history","snapshot","get","--help"]);
        help.Exit.Should().Be(0);help.Output.Should().Contain("--session-token").And.Contain("--workspace-id");
        help.Output.Should().NotContain("--request").And.NotContain("--provider").And.NotContain("--download");
    }

    [Theory]
    [InlineData("market-history.v2.schema.json","809e904513e07f4696c38386491250e096df922539109e14a3c570f804d68c3f")]
    [InlineData("market-coverage.v2.schema.json","08d40dbbba499036cb2d756603ceaa6d01fea5d5decc73f72921b26f836ec8c1")]
    [InlineData("samples/market-history.v2.sample.json","25e675c85894acbe6f55788f147ca8abe2eccb22adc3899dcd3cee905ac64a02")]
    [InlineData("samples/market-coverage.v2.sample.json","91e3c72479a06074a28ad3e6a3d3157bdbe5d6db7cf8068d915e07b6544b4e17")]
    public void PublicArtifactsAreExactProducerBytes(string relative,string expected)
    {
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root(),"contracts",relative)))).Should().Be(expected);
    }

    private sealed class Factory(Handler handler):IApiClientFactory
    {
        public IWickdApiClient CreateClient(GlobalCommandSettings settings)=>Client(handler);
        public WickdCliConfig GetEffectiveConfig(GlobalCommandSettings settings)=>new();
    }
    private static async Task<(int Exit,string Output)> Run(Handler handler,string[] args)
    {
        using var output=new StringWriter();
        var console=AnsiConsole.Create(new AnsiConsoleSettings{Ansi=AnsiSupport.No,ColorSystem=ColorSystemSupport.NoColors,Interactive=InteractionSupport.No,Out=new AnsiConsoleOutput(output)});
        console.Profile.Width=500;
        var services=new ServiceCollection();services.AddSingleton<IApiClientFactory>(new Factory(handler));
        var prior=AnsiConsole.Console;
        try
        {
            AnsiConsole.Console=console;
            var app=Program.CreateCommandApp(new TypeRegistrar(services),console);
            return(await app.RunAsync(args),output.ToString());
        }
        finally{AnsiConsole.Console=prior;}
    }
}
