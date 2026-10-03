using Wickd.Cli.Models;

namespace Wickd.Cli.Rendering;

public interface IConsoleRenderer
{
    void RenderSuccess(string message);
    void RenderError(string message, Exception? ex = null);
    void RenderWarning(string message);
    void RenderJson<T>(T data);
    void RenderDatasets(IEnumerable<DatasetAliasDto> datasets);
    void RenderRuns(IEnumerable<RunListingDto> runs);
    void RenderTrades(IEnumerable<TradeSummaryDto> trades);
    void RenderAccounts(IEnumerable<AccountDto> accounts);
    void RenderAccountRisk(AccountRiskDto risk);
    void RenderVwapAnalysis(VwapAnalysisResultDto result);
    void RenderBacktestResult(BacktestResultDto result);
    void RenderFetchResult(FetchResultDto result);
}
