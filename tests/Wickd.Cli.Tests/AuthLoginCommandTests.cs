using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Wickd.Cli.Common;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli.Tests;

public sealed class AuthLoginCommandTests : IDisposable
{
    private readonly string _tempDirectory;

    public AuthLoginCommandTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "wickd_auth_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public async Task Login_WithPositionalToken_HealthChecksWithTheEnteredTokenNotTheDiskToken()
    {
        const string staleDiskToken = "stale-disk-token";
        const string enteredToken = "entered-login-token";
        var configPath = Path.Combine(_tempDirectory, "config.json");

        var configManager = new ConfigManager();
        configManager.SaveConfig(
            new WickdCliConfig
            {
                ApiUrl = "https://api.wickdalgo.test",
                ApiToken = staleDiskToken
            },
            configPath);

        var handler = new CapturingHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"schemaVersion":1,"contract":"supported-instruments","timeframes":["4h"],"instruments":[{"marketId":"BTC_USDT_PERP","exchangeId":"binance","exchangeSymbol":"BTC/USDT:USDT"}]}
                    """,
                    Encoding.UTF8,
                    "application/json")
            }
        };

        var factory = new ApiClientFactory(new StubHttpClientFactory(new HttpClient(handler)), configManager);
        var renderer = new RecordingConsoleRenderer();
        var app = Program.CreateCommandApp(CreateRegistrar(configManager, factory, renderer), CreateConsole());

        var exitCode = await app.RunAsync(["auth", "login", enteredToken, "--config", configPath]);

        exitCode.Should().Be(ExitCodes.Success);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/instruments");
        handler.LastRequest.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(enteredToken);
        handler.LastRequest.Headers.Authorization.Parameter.Should().NotBe(staleDiskToken);
        configManager.LoadConfig(configPath).ApiToken.Should().Be(enteredToken);
        renderer.Successes.Should().Contain(message => message.Contains("saved successfully"));
    }

    private static ITypeRegistrar CreateRegistrar(
        IConfigManager configManager,
        IApiClientFactory factory,
        IConsoleRenderer renderer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configManager);
        services.AddSingleton(factory);
        services.AddSingleton(renderer);
        return new TypeRegistrar(services);
    }

    private static IAnsiConsole CreateConsole()
    {
        return AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(new StringWriter())
        });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(ResponseToReturn);
        }
    }

    private sealed class RecordingConsoleRenderer : IConsoleRenderer
    {
        public List<string> Successes { get; } = [];

        public void RenderBanner() { }
        public void RenderSuccess(string message) => Successes.Add(message);
        public void RenderError(string message, Exception? ex = null) { }
        public void RenderWarning(string message) { }
        public void RenderInfo(string message) { }
        public void RenderJson<T>(T data) { }
        public void RenderDatasets(IEnumerable<DatasetAliasDto> datasets) { }
        public void RenderRuns(IEnumerable<RunListingDto> runs) { }
        public void RenderTrades(IEnumerable<TradeSummaryDto> trades) { }
        public void RenderAccounts(IEnumerable<AccountDto> accounts) { }
        public void RenderAccountRisk(AccountRiskDto risk) { }
        public void RenderVwapAnalysis(VwapAnalysisResultDto result) { }
        public void RenderBacktestResult(BacktestResultDto result) { }
        public void RenderFetchResult(FetchResultDto result) { }
    }
}
