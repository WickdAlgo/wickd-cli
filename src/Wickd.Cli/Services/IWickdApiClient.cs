using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public interface IWickdApiClient
{
    Task<FetchResultDto> FetchCandlesAsync(FetchHistoricalCandlesRequest request, CancellationToken cancellationToken = default);
    Task<SupportedInstrumentsPayload> GetSupportedInstrumentsAsync(CancellationToken cancellationToken = default);
    Task<BacktestResultDto> RunBacktestAsync(BacktestRequest request, CancellationToken cancellationToken = default);
    Task<VwapAnalysisResultDto> AnalyzeVwapAsync(VwapAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<List<DatasetAliasDto>> GetDatasetAliasesAsync(CancellationToken cancellationToken = default);
    Task<DatasetAliasDto> SaveDatasetAliasAsync(SaveDatasetAliasRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteDatasetAliasAsync(string alias, CancellationToken cancellationToken = default);
    Task<List<RunListingDto>> GetRunsAsync(CancellationToken cancellationToken = default);
    Task<InspectionRunDto?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<List<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<AccountRiskDto?> GetAccountRiskAsync(string accountId, CancellationToken cancellationToken = default);
    Task<List<TradeSummaryDto>> GetTradesAsync(CancellationToken cancellationToken = default);
    Task<TradeDetailDto?> GetTradeAsync(string tradeId, CancellationToken cancellationToken = default);
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}
