using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wickd.Cli.Configuration;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient : IWickdApiClient
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

        _httpClient.DefaultRequestHeaders.Authorization = null;
        if (!string.IsNullOrWhiteSpace(_config.ApiToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiToken);
        }

        SetValidatedHeader("X-Wickd-Session", _config.SessionToken);
        SetValidatedHeader("X-Wickd-Workspace", _config.WorkspaceId);
    }

    private void SetValidatedHeader(string name, string? value)
    {
        _httpClient.DefaultRequestHeaders.Remove(name);
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        var maximumLength = name == "X-Wickd-Workspace" ? 128 : 2048;
        if (trimmed.Length > maximumLength || trimmed.Any(char.IsControl))
            throw new ArgumentException($"Configured {name} is invalid.");
        _httpClient.DefaultRequestHeaders.Add(name, trimmed);
    }

    private void RequireV2Credentials(bool requireWorkspace)
    {
        if (string.IsNullOrWhiteSpace(_config.SessionToken))
            throw new InvalidOperationException("A Wickd user session is required. Set sessionToken in config or pass --session-token.");
        if (requireWorkspace && string.IsNullOrWhiteSpace(_config.WorkspaceId))
            throw new InvalidOperationException("A workspace is required. Select one with 'wickd workspace use <ID>' or pass --workspace-id.");
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
        var dataset = request.Dataset ?? new CachedDatasetSelector
        {
            MarketId = request.MarketId,
            ExchangeId = request.ExchangeId,
            ExchangeSymbol = request.ExchangeSymbol,
            Timeframe = request.Timeframe
        };
        if (!string.IsNullOrWhiteSpace(dataset.Alias))
        {
            dataset = await ResolveAliasAsync(dataset.Alias, cancellationToken);
        }

        var response = await _httpClient.PostAsJsonAsync("api/runs", new StructureRunRequestDto
        {
            RunId = request.RunId,
            Dataset = dataset,
            PivotStrength = request.PivotStrength
        }, JsonOptions, cancellationToken);
        return RequireContract(
            await HandleResponseAsync<BacktestResultDto>(response, "api/runs", cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "run");
    }

    private async Task<CachedDatasetSelector> ResolveAliasAsync(string alias, CancellationToken cancellationToken)
    {
        var aliases = await GetDatasetAliasesAsync(cancellationToken);
        var selectedAlias = aliases.SingleOrDefault(item => item.Alias.Equals(alias, StringComparison.Ordinal));
        if (selectedAlias is null)
            throw new InvalidOperationException($"Dataset alias '{alias}' was not found.");

        var datasets = await GetDatasetsAsync(cancellationToken);
        var matches = datasets.Where(item =>
                item.MarketId.Equals(selectedAlias.MarketId, StringComparison.Ordinal)
                && item.ExchangeId.Equals(selectedAlias.ExchangeId, StringComparison.Ordinal)
                && item.Timeframe.Equals(selectedAlias.Timeframe, StringComparison.Ordinal)
                && item.FromUtc == selectedAlias.FromUtc
                && item.ToUtc == selectedAlias.ToUtc)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                matches.Length == 0
                    ? $"Dataset alias '{alias}' does not resolve to a cached dataset identity."
                    : $"Dataset alias '{alias}' resolves to multiple cached dataset identities.");

        var match = matches[0];
        return new CachedDatasetSelector
        {
            MarketId = match.MarketId,
            ExchangeId = match.ExchangeId,
            ExchangeSymbol = match.ExchangeSymbol,
            Timeframe = match.Timeframe,
            FromUtc = match.FromUtc,
            ToUtc = match.ToUtc
        };
    }

    private async Task<List<DatasetListingDto>> GetDatasetsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/datasets", cancellationToken);
        var datasets = await HandleResponseAsync<List<DatasetListingDto>>(response, "api/datasets", cancellationToken);
        return datasets.Select(dataset => RequireContract(
                dataset,
                item => (item.SchemaVersion, item.Contract),
                "dataset-listing"))
            .ToList();
    }

    public async Task<WorkspaceListPayloadDto> GetWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        RequireV2Credentials(requireWorkspace: false);
        var response = await _httpClient.GetAsync("api/v2/workspaces", cancellationToken);
        return RequireV2Contract(
            await HandleResponseAsync<WorkspaceListPayloadDto>(response, "api/v2/workspaces", cancellationToken),
            "workspace-list");
    }

    public async Task<WorkspaceDetailPayloadDto> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        RequireV2Credentials(requireWorkspace: false);
        var path = $"api/v2/workspaces/{Uri.EscapeDataString(workspaceId)}";
        var response = await _httpClient.GetAsync(path, cancellationToken);
        return RequireV2Contract(
            await HandleResponseAsync<WorkspaceDetailPayloadDto>(response, path, cancellationToken),
            "workspace-detail");
    }

    public async Task<StrategyListPayloadDto> GetStrategiesAsync(CancellationToken cancellationToken = default)
    {
        RequireV2Credentials(requireWorkspace: true);
        List<StrategySummaryDto> items = [];
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        long? watermark = null;
        StrategyListPayloadDto? lastPage = null;
        do
        {
            var query = cursor is null ? "?limit=50" : $"?limit=50&cursor={Uri.EscapeDataString(cursor)}";
            var response = await _httpClient.GetAsync("api/v2/strategies" + query, cancellationToken);
            var page = RequireV2Contract(
                await HandleResponseAsync<StrategyListPayloadDto>(response, "api/v2/strategies", cancellationToken),
                "strategy-list-page");
            if (watermark.HasValue && watermark.Value != page.Data.PublicationWatermark)
                throw new InvalidOperationException("Strategy pages changed publication watermark during pagination.");
            watermark = page.Data.PublicationWatermark;
            items.AddRange(page.Data.Items);
            lastPage = page;
            cursor = page.Data.NextCursor;
            if (cursor is not null && !seenCursors.Add(cursor))
                throw new InvalidOperationException("Strategy API repeated a pagination cursor.");
        } while (cursor is not null);

        if (lastPage is null)
            throw new InvalidOperationException("Strategy API returned no page.");
        lastPage.Data = new StrategyListPageDto
        {
            Items = items,
            NextCursor = null,
            PublicationWatermark = watermark ?? 0
        };
        return lastPage;
    }

    public async Task<StrategyDetailPayloadDto> GetStrategyAsync(string strategyId, CancellationToken cancellationToken = default)
    {
        RequireV2Credentials(requireWorkspace: true);
        var path = $"api/v2/strategies/{Uri.EscapeDataString(strategyId)}";
        var response = await _httpClient.GetAsync(path, cancellationToken);
        return RequireV2Contract(
            await HandleResponseAsync<StrategyDetailPayloadDto>(response, path, cancellationToken),
            "strategy-detail");
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

    public async Task DeleteDatasetAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        var uri = $"api/dataset-aliases/{Uri.EscapeDataString(alias)}";
        var response = await _httpClient.DeleteAsync(uri, cancellationToken);
        _ = RequireContract(
            await HandleResponseAsync<DatasetAliasDto>(response, uri, cancellationToken),
            result => (result.SchemaVersion, result.Contract),
            "dataset-alias");
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

    private static T RequireV2Contract<T>(T value, string expectedContract)
    {
        var identity = value switch
        {
            WorkspaceListPayloadDto workspace => (workspace.SchemaVersion, workspace.Contract),
            WorkspaceDetailPayloadDto workspace => (workspace.SchemaVersion, workspace.Contract),
            StrategyListPayloadDto strategies => (strategies.SchemaVersion, strategies.Contract),
            StrategyDetailPayloadDto strategy => (strategy.SchemaVersion, strategy.Contract),
            _ => throw new InvalidOperationException("Unsupported v2 response type.")
        };
        if (identity.Item1 != 2 || !identity.Item2.Equals(expectedContract, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported API contract '{identity.Item2}' v{identity.Item1}; expected '{expectedContract}' v2.");
        return value;
    }
}
