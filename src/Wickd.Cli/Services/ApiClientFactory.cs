using Wickd.Cli.Commands.Settings;
using Wickd.Cli.Configuration;

namespace Wickd.Cli.Services;

public interface IApiClientFactory
{
    IWickdApiClient CreateClient(GlobalCommandSettings settings);
    WickdCliConfig GetEffectiveConfig(GlobalCommandSettings settings);
}

public sealed class ApiClientFactory : IApiClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfigManager _configManager;

    public ApiClientFactory(IHttpClientFactory httpClientFactory, IConfigManager configManager)
    {
        _httpClientFactory = httpClientFactory;
        _configManager = configManager;
    }

    public WickdCliConfig GetEffectiveConfig(GlobalCommandSettings settings)
    {
        var config = _configManager.LoadConfig(settings.ConfigPath);

        if (!string.IsNullOrWhiteSpace(settings.ApiUrl))
        {
            config.ApiUrl = settings.ApiUrl;
        }

        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            config.ApiToken = settings.Token;
        }

        return config;
    }

    public IWickdApiClient CreateClient(GlobalCommandSettings settings)
    {
        var config = GetEffectiveConfig(settings);
        var httpClient = _httpClientFactory.CreateClient("WickdApi");
        return new WickdApiClient(httpClient, config);
    }
}
