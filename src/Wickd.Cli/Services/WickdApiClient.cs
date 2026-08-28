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
        return RequireContract(
            await HandleResponseAsync<FetchResultDto>(response, "api/fetch", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "fetch");
    }

    public async Task<SupportedInstrumentsPayload> GetSupportedInstrumentsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/instruments", cancellationToken);
        return RequireContract(
            await HandleResponseAsync<SupportedInstrumentsPayload>(response, "api/instruments", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "supported-instruments");
    }

    public async Task<BacktestResultDto> RunBacktestAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/backtest", request, JsonOptions, cancellationToken);
        return RequireContract(
            await HandleResponseAsync<BacktestResultDto>(response, "api/backtest", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "backtest");
    }

    public async Task<VwapAnalysisResultDto> AnalyzeVwapAsync(VwapAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/analyze/vwap", request, JsonOptions, cancellationToken);
        return RequireContract(
            await HandleResponseAsync<VwapAnalysisResultDto>(response, "api/analyze/vwap", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "vwap-analysis");
    }

    public async Task<List<DatasetAliasDto>> GetDatasetAliasesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/dataset-aliases", cancellationToken);
        var aliases = await HandleResponseAsync<List<DatasetAliasDto>>(response, "api/dataset-aliases", cancellationToken);
        return aliases.Select(alias => RequireContract(
                alias,
                item => (item.SchemaVersion, item.Contract),
                "dataset-alias"))
            .ToList();
    }

    public async Task<DatasetAliasDto> SaveDatasetAliasAsync(SaveDatasetAliasRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/dataset-aliases", request, JsonOptions, cancellationToken);
        return RequireContract(
            await HandleResponseAsync<DatasetAliasDto>(response, "api/dataset-aliases", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "dataset-alias");
    }

    public async Task<bool> DeleteDatasetAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        var uri = $"api/dataset-aliases/{Uri.EscapeDataString(alias)}";
        var response = await _httpClient.DeleteAsync(uri, cancellationToken);
        _ = RequireContract(
            await HandleResponseAsync<DatasetAliasDto>(response, uri, cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "dataset-alias");
        return true;
    }

    public async Task<List<RunListingDto>> GetRunsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/runs", cancellationToken);
        var runs = await HandleResponseAsync<List<RunListingDto>>(response, "api/runs", cancellationToken);
        return runs.Select(run => RequireContract(
                run,
                item => (item.SchemaVersion, item.Contract),
                "run-listing"))
            .ToList();
    }

    public async Task<InspectionRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/runs/{Uri.EscapeDataString(runId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        return RequireContract(
            await HandleResponseAsync<InspectionRunDto>(response, $"api/runs/{runId}", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "inspection-dataset");
    }

    public async Task<AccountsPayloadDto> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/accounts", cancellationToken);
        return RequireContract(
            await HandleResponseAsync<AccountsPayloadDto>(response, "api/accounts", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "accounts");
    }

    public async Task<AccountRiskDto?> GetAccountRiskAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/accounts/{Uri.EscapeDataString(accountId)}/risk", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        return RequireContract(
            await HandleResponseAsync<AccountRiskDto>(response, $"api/accounts/{accountId}/risk", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "account-risk");
    }

    public async Task<List<TradeSummaryDto>> GetTradesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("api/trades", cancellationToken);
        var trades = await HandleResponseAsync<List<TradeSummaryDto>>(response, "api/trades", cancellationToken);
        return trades.Select(trade => RequireContract(
                trade,
                item => (item.SchemaVersion, item.Contract),
                "trade-summary"))
            .ToList();
    }

    public async Task<TradeDetailDto?> GetTradeAsync(string tradeId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/trades/{Uri.EscapeDataString(tradeId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        return RequireContract(
            await HandleResponseAsync<TradeDetailDto>(response, $"api/trades/{tradeId}", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "trade-detail");
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await GetSupportedInstrumentsAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static T RequireContract<T>(
        T value,
        Func<T, (int SchemaVersion, string Contract)> identity,
        string expectedContract)
    {
        var (schemaVersion, contract) = identity(value);
        if (schemaVersion != 1 || !contract.Equals(expectedContract, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported API contract '{contract}' v{schemaVersion}; expected '{expectedContract}' v1.");
        }

        return value;
    }
}
