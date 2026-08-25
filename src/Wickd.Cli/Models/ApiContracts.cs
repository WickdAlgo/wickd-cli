using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wickd.Cli.Models;

public sealed class FetchHistoricalCandlesRequest
{
    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset ToUtc { get; set; }

    [JsonPropertyName("exchangeId")]
    public string ExchangeId { get; set; } = "binance";

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("force")]
    public bool Force { get; set; }
}

public sealed class FetchResultDto
{
    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset ToUtc { get; set; }

    [JsonPropertyName("candlesFetched")]
    public int CandlesFetched { get; set; }

    [JsonPropertyName("gaps")]
    public int Gaps { get; set; }

    [JsonPropertyName("cachePath")]
    public string? CachePath { get; set; }

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }
}

public sealed class BacktestRequest
{
    [JsonPropertyName("marketId")]
    public string? MarketId { get; set; }

    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; set; }

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset? FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset? ToUtc { get; set; }

    [JsonPropertyName("datasetAlias")]
    public string? DatasetAlias { get; set; }

    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    [JsonPropertyName("pivotStrength")]
    public int? PivotStrength { get; set; }
}

public sealed class BacktestResultDto
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset ToUtc { get; set; }

    [JsonPropertyName("candlesCount")]
    public int CandlesCount { get; set; }

    [JsonPropertyName("structureEventsCount")]
    public int StructureEventsCount { get; set; }

    [JsonPropertyName("manifestPath")]
    public string? ManifestPath { get; set; }

    [JsonPropertyName("eventsPath")]
    public string? EventsPath { get; set; }

    [JsonPropertyName("structures")]
    public List<StructureEventDto> Structures { get; set; } = [];
}

public sealed class StructureEventDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("openTimeUtc")]
    public DateTimeOffset? OpenTimeUtc { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }
}

public sealed class VwapAnalysisRequest
{
    [JsonPropertyName("marketId")]
    public string? MarketId { get; set; }

    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; set; }

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset? FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset? ToUtc { get; set; }

    [JsonPropertyName("datasetAlias")]
    public string? DatasetAlias { get; set; }

    [JsonPropertyName("periods")]
    public List<string>? Periods { get; set; }

    [JsonPropertyName("levelPeriods")]
    public List<string>? LevelPeriods { get; set; }
}

public sealed class VwapPeriodSummaryDto
{
    [JsonPropertyName("period")]
    public string Period { get; set; } = string.Empty;

    [JsonPropertyName("currentVwap")]
    public decimal? CurrentVwap { get; set; }

    [JsonPropertyName("previousClose")]
    public decimal? PreviousClose { get; set; }

    [JsonPropertyName("isSwept")]
    public bool IsSwept { get; set; }

    [JsonPropertyName("levelsCount")]
    public int LevelsCount { get; set; }

    [JsonPropertyName("sweptLevelsCount")]
    public int SweptLevelsCount { get; set; }

    [JsonPropertyName("expiredLevelsCount")]
    public int ExpiredLevelsCount { get; set; }
}

public sealed class VolumeClassificationSummaryDto
{
    [JsonPropertyName("large")]
    public int Large { get; set; }

    [JsonPropertyName("medium")]
    public int Medium { get; set; }

    [JsonPropertyName("low")]
    public int Low { get; set; }

    [JsonPropertyName("none")]
    public int None { get; set; }
}

public sealed class VwapAnalysisResultDto
{
    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset ToUtc { get; set; }

    [JsonPropertyName("candlesAnalyzed")]
    public int CandlesAnalyzed { get; set; }

    [JsonPropertyName("gaps")]
    public int Gaps { get; set; }

    [JsonPropertyName("summaries")]
    public List<VwapPeriodSummaryDto> Summaries { get; set; } = [];

    [JsonPropertyName("volumeSummary")]
    public VolumeClassificationSummaryDto VolumeSummary { get; set; } = new();

    [JsonPropertyName("levels")]
    public List<VwapLevelDto> Levels { get; set; } = [];

    [JsonPropertyName("classifications")]
    public List<VwapClassificationDto> Classifications { get; set; } = [];

    [JsonPropertyName("points")]
    public List<VwapPointDto> Points { get; set; } = [];
}

public sealed class VwapLevelDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "level";

    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("exchangeId")]
    public string ExchangeId { get; set; } = "binance";

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("period")]
    public string Period { get; set; } = string.Empty;

    [JsonPropertyName("openTimeUtc")]
    public DateTimeOffset? OpenTimeUtc { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("bornAtUtc")]
    public DateTimeOffset? BornAtUtc { get; set; }

    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    [JsonPropertyName("sweptAtUtc")]
    public DateTimeOffset? SweptAtUtc { get; set; }

    [JsonPropertyName("runningVwap")]
    public decimal? RunningVwap { get; set; }

    [JsonPropertyName("previousClose")]
    public decimal? PreviousClose { get; set; }

    [JsonPropertyName("volumeClass")]
    public string? VolumeClass { get; set; }

    [JsonPropertyName("isUp")]
    public bool? IsUp { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }
}

public sealed class VwapClassificationDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "classification";

    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("exchangeId")]
    public string ExchangeId { get; set; } = "binance";

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("period")]
    public string? Period { get; set; }

    [JsonPropertyName("openTimeUtc")]
    public DateTimeOffset? OpenTimeUtc { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("bornAtUtc")]
    public DateTimeOffset? BornAtUtc { get; set; }

    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    [JsonPropertyName("sweptAtUtc")]
    public DateTimeOffset? SweptAtUtc { get; set; }

    [JsonPropertyName("runningVwap")]
    public decimal? RunningVwap { get; set; }

    [JsonPropertyName("previousClose")]
    public decimal? PreviousClose { get; set; }

    [JsonPropertyName("volumeClass")]
    public string? VolumeClass { get; set; }

    [JsonPropertyName("isUp")]
    public bool? IsUp { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }
}

public sealed class VwapPointDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "point";

    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("exchangeId")]
    public string ExchangeId { get; set; } = "binance";

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("period")]
    public string? Period { get; set; }

    [JsonPropertyName("openTimeUtc")]
    public DateTimeOffset? OpenTimeUtc { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("bornAtUtc")]
    public DateTimeOffset? BornAtUtc { get; set; }

    [JsonPropertyName("expiresAtUtc")]
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    [JsonPropertyName("sweptAtUtc")]
    public DateTimeOffset? SweptAtUtc { get; set; }

    [JsonPropertyName("runningVwap")]
    public decimal? RunningVwap { get; set; }

    [JsonPropertyName("previousClose")]
    public decimal? PreviousClose { get; set; }

    [JsonPropertyName("volumeClass")]
    public string? VolumeClass { get; set; }

    [JsonPropertyName("isUp")]
    public bool? IsUp { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }
}

public sealed class DatasetAliasDto
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("exchangeId")]
    public string ExchangeId { get; set; } = "binance";

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset ToUtc { get; set; }

    [JsonPropertyName("candlesCount")]
    public int CandlesCount { get; set; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset? CreatedAtUtc { get; set; }
}

public sealed class SaveDatasetAliasRequest
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("marketId")]
    public string MarketId { get; set; } = string.Empty;

    [JsonPropertyName("exchangeId")]
    public string ExchangeId { get; set; } = "binance";

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = string.Empty;

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset ToUtc { get; set; }

    [JsonPropertyName("force")]
    public bool Force { get; set; }
}

public sealed class RunListingDto
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("marketId")]
    public string? MarketId { get; set; }

    [JsonPropertyName("timeframe")]
    public string? Timeframe { get; set; }

    [JsonPropertyName("fromUtc")]
    public DateTimeOffset? FromUtc { get; set; }

    [JsonPropertyName("toUtc")]
    public DateTimeOffset? ToUtc { get; set; }

    [JsonPropertyName("candlesCount")]
    public int? CandlesCount { get; set; }

    [JsonPropertyName("structureEventsCount")]
    public int? StructureEventsCount { get; set; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset? CreatedAtUtc { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class InspectionRunDto
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("manifest")]
    public JsonElement? Manifest { get; set; }

    [JsonPropertyName("structures")]
    public List<StructureEventDto> Structures { get; set; } = [];
}

public sealed class AccountDto
{
    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("exchangeId")]
    public string ExchangeId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("balance")]
    public decimal Balance { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USDT";

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
}

public sealed class AccountRiskDto
{
    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("totalEquity")]
    public decimal TotalEquity { get; set; }

    [JsonPropertyName("utilizedMargin")]
    public decimal UtilizedMargin { get; set; }

    [JsonPropertyName("freeMargin")]
    public decimal FreeMargin { get; set; }

    [JsonPropertyName("openPositionsCount")]
    public int OpenPositionsCount { get; set; }

    [JsonPropertyName("riskScore")]
    public double RiskScore { get; set; }
}

public sealed class TradeSummaryDto
{
    [JsonPropertyName("tradeId")]
    public string TradeId { get; set; } = string.Empty;

    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = string.Empty;

    [JsonPropertyName("entryPrice")]
    public decimal EntryPrice { get; set; }

    [JsonPropertyName("exitPrice")]
    public decimal? ExitPrice { get; set; }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("pnl")]
    public decimal? Pnl { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("openedAtUtc")]
    public DateTimeOffset OpenedAtUtc { get; set; }

    [JsonPropertyName("closedAtUtc")]
    public DateTimeOffset? ClosedAtUtc { get; set; }
}

public sealed class TradeDetailDto
{
    [JsonPropertyName("tradeId")]
    public string TradeId { get; set; } = string.Empty;

    [JsonPropertyName("accountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = string.Empty;

    [JsonPropertyName("entryPrice")]
    public decimal EntryPrice { get; set; }

    [JsonPropertyName("exitPrice")]
    public decimal? ExitPrice { get; set; }

    [JsonPropertyName("stopLoss")]
    public decimal? StopLoss { get; set; }

    [JsonPropertyName("takeProfit")]
    public decimal? TakeProfit { get; set; }

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("pnl")]
    public decimal? Pnl { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("openedAtUtc")]
    public DateTimeOffset OpenedAtUtc { get; set; }

    [JsonPropertyName("closedAtUtc")]
    public DateTimeOffset? ClosedAtUtc { get; set; }
}
