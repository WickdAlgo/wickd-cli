using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed class WickdApiClient : IWickdApiClient
{
    private readonly HttpClient _httpClient;
    private readonly WickdCliConfig _config;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WickdApiClient(HttpClient httpClient, WickdCliConfig config)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var baseUrl = _config.ApiUrl.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds > 0 ? _config.TimeoutSeconds : 60);

        if (!string.IsNullOrWhiteSpace(_config.ApiToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiToken);
        }
    }

    private async Task<T> HandleResponseAsync<T>(HttpResponseMessage response, string endpoint, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            if (content == null)
            {
                throw new InvalidOperationException($"API response from {endpoint} was null.");
            }
            return content;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;
        throw new HttpRequestException($"API request to {endpoint} failed with status {statusCode} ({response.ReasonPhrase}): {errorBody}", null, response.StatusCode);
    }

    public async Task<FetchResultDto> FetchCandlesAsync(FetchHistoricalCandlesRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/fetch", request, JsonOptions, cancellationToken);
        return await HandleResponseAsync<FetchResultDto>(response, "api/fetch", cancellationToken);
    }

    public async Task<BacktestResultDto> RunBacktestAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/backtest", request, JsonOptions, cancellationToken);
        return await HandleResponseAsync<BacktestResultDto>(response, "api/backtest", cancellationToken);
    }

    public async Task<VwapAnalysisResultDto> AnalyzeVwapAsync(VwapAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/analyze/vwap", request, JsonOptions, cancellationToken);
        return await HandleResponseAsync<VwapAnalysisResultDto>(response, "api/analyze/vwap", cancellationToken);
    }

    public async Task<List<DatasetAliasDto>> GetDatasetAliasesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/dataset-aliases", cancellationToken);
        return await HandleResponseAsync<List<DatasetAliasDto>>(response, "api/dataset-aliases", cancellationToken);
    }

    public async Task<DatasetAliasDto> SaveDatasetAliasAsync(SaveDatasetAliasRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/dataset-aliases", request, JsonOptions, cancellationToken);
        return await HandleResponseAsync<DatasetAliasDto>(response, "api/dataset-aliases", cancellationToken);
    }

    public async Task<bool> DeleteDatasetAliasAsync(string alias, bool deleteCache = false, CancellationToken cancellationToken = default)
    {
        var uri = $"api/dataset-aliases/{Uri.EscapeDataString(alias)}?deleteCache={deleteCache.ToString().ToLowerInvariant()}";
        var response = await _httpClient.DeleteAsync(uri, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<RunListingDto>> GetRunsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/runs", cancellationToken);
        return await HandleResponseAsync<List<RunListingDto>>(response, "api/runs", cancellationToken);
    }

    public async Task<InspectionRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/runs/{Uri.EscapeDataString(runId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        return await HandleResponseAsync<InspectionRunDto>(response, $"api/runs/{runId}", cancellationToken);
    }

    public async Task<bool> DeleteRunAsync(string runId, bool force = false, CancellationToken cancellationToken = default)
    {
        var uri = $"api/runs/{Uri.EscapeDataString(runId)}?force={force.ToString().ToLowerInvariant()}";
        var response = await _httpClient.DeleteAsync(uri, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/accounts", cancellationToken);
        return await HandleResponseAsync<List<AccountDto>>(response, "api/accounts", cancellationToken);
    }

    public async Task<AccountRiskDto?> GetAccountRiskAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/accounts/{Uri.EscapeDataString(accountId)}/risk", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        return await HandleResponseAsync<AccountRiskDto>(response, $"api/accounts/{accountId}/risk", cancellationToken);
    }

    public async Task<List<TradeSummaryDto>> GetTradesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/trades", cancellationToken);
        return await HandleResponseAsync<List<TradeSummaryDto>>(response, "api/trades", cancellationToken);
    }

    public async Task<TradeDetailDto?> GetTradeAsync(string tradeId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/trades/{Uri.EscapeDataString(tradeId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        return await HandleResponseAsync<TradeDetailDto>(response, $"api/trades/{tradeId}", cancellationToken);
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/instruments", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
