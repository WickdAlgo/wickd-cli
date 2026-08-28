using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wickd.Cli.Models;

public sealed class FetchHistoricalCandlesRequest
{
    public string MarketId { get; set; } = string.Empty;
    public string ExchangeId { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
}

public sealed class FetchResultDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public string MarketId { get; set; } = string.Empty;
    public string ExchangeId { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public int CandleCount { get; set; }
    public bool CacheHit { get; set; }
}

public sealed class CachedDatasetSelector
{
    public string? Alias { get; set; }
    public string? MarketId { get; set; }
    public string? ExchangeId { get; set; }
    public string? ExchangeSymbol { get; set; }
    public string? Timeframe { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
}

public sealed class BacktestRequest
{
    public string RunId { get; set; } = string.Empty;
    public string? MarketId { get; set; }
    public string? ExchangeId { get; set; }
    public string? ExchangeSymbol { get; set; }
    public string? Timeframe { get; set; }
    public IReadOnlyList<JsonElement>? Candles { get; set; }
    public CachedDatasetSelector? Dataset { get; set; }
    public int PivotStrength { get; set; } = 2;
}

public sealed class BacktestResultDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string MarketId { get; set; } = string.Empty;
    public string ExchangeId { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public int CandleCount { get; set; }
    public int EventCount { get; set; }
    public int GapCount { get; set; }
}

public sealed class VwapAnalysisRequest
{
    public string? MarketId { get; set; }
    public string? ExchangeId { get; set; }
    public string? Timeframe { get; set; }
    public IReadOnlyList<JsonElement>? Candles { get; set; }
    public CachedDatasetSelector? Dataset { get; set; }
    public VwapAnalysisSettingsDto? Settings { get; set; }
}

public sealed class VwapAnalysisSettingsDto
{
    public List<string> EnabledPeriods { get; set; } = [];
    public List<string> PreviousLevelPeriods { get; set; } = [];
    public int VolumeLength { get; set; }

    [JsonConverter(typeof(DecimalStringJsonConverter))]
    public decimal MediumThreshold { get; set; }

    [JsonConverter(typeof(DecimalStringJsonConverter))]
    public decimal LargeThreshold { get; set; }

    [JsonConverter(typeof(DecimalStringJsonConverter))]
    public decimal LowVolumeThreshold { get; set; }

    public bool ShowLowVolume { get; set; }
}

public sealed class VwapAnalysisResultDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public string MarketId { get; set; } = string.Empty;
    public string ExchangeId { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public int CandleCount { get; set; }
    public int GapCount { get; set; }
    public List<VwapSeriesDto> Series { get; set; } = [];
    public List<VwapLevelDto> Levels { get; set; } = [];
    public List<VwapClassificationDto> Classifications { get; set; } = [];
}

public sealed class VwapSeriesDto
{
    public string Period { get; set; } = string.Empty;
    public List<VwapPointDto> Points { get; set; } = [];
}

public sealed class VwapPointDto
{
    public DateTimeOffset OpenTimeUtc { get; set; }

    [JsonConverter(typeof(NullableDecimalStringJsonConverter))]
    public decimal? RunningVwap { get; set; }

    [JsonConverter(typeof(NullableDecimalStringJsonConverter))]
    public decimal? PreviousClose { get; set; }
}

public sealed class VwapLevelDto
{
    public string Period { get; set; } = string.Empty;

    [JsonConverter(typeof(DecimalStringJsonConverter))]
    public decimal Price { get; set; }

    public DateTimeOffset BornAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? SweptAtUtc { get; set; }
}

public sealed class VwapClassificationDto
{
    public DateTimeOffset OpenTimeUtc { get; set; }
    public string VolumeClass { get; set; } = string.Empty;
    public bool IsUp { get; set; }

    [JsonConverter(typeof(NullableDecimalStringJsonConverter))]
    public decimal? Score { get; set; }
}

public sealed class DatasetAliasDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string MarketId { get; set; } = string.Empty;
    public string ExchangeId { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
}

public sealed class SaveDatasetAliasRequest
{
    public string Alias { get; set; } = string.Empty;
    public string MarketId { get; set; } = string.Empty;
    public string ExchangeId { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public bool Force { get; set; }
}

public sealed class SupportedInstrumentsPayload
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public List<SupportedInstrumentDto> Instruments { get; set; } = [];
    public List<string> Timeframes { get; set; } = [];
}

public sealed class SupportedInstrumentDto
{
    public string MarketId { get; set; } = string.Empty;
    public string ExchangeId { get; set; } = string.Empty;
    public string ExchangeSymbol { get; set; } = string.Empty;
}

public sealed class RunListingDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public bool HasDataset { get; set; }
    public DateTimeOffset LastWrittenAtUtc { get; set; }
    public InspectionRunInstrumentDto? Instrument { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public int? CandleCount { get; set; }
    public string? ApplicationVersion { get; set; }
    public string? DatasetAlias { get; set; }
}

public sealed class InspectionRunInstrumentDto
{
    public string Market { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
}

public sealed class InspectionRunDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Data { get; set; } = [];
}

public sealed class AccountsPayloadDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public List<AccountDto> Accounts { get; set; } = [];
}

public sealed class AccountDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public List<AccountEquityObservationDto> EquityObservations { get; set; } = [];
}

public sealed class AccountEquityObservationDto
{
    [JsonConverter(typeof(DecimalStringJsonConverter))]
    public decimal Equity { get; set; }

    public DateTimeOffset ObservedAtUtc { get; set; }
}

public sealed class AccountRiskDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public DateTimeOffset AsOfUtc { get; set; }

    [JsonConverter(typeof(DecimalStringJsonConverter))]
    public decimal OpenRisk { get; set; }

    public List<JsonElement> ConcurrentRiskWarnings { get; set; } = [];
}

public sealed class TradeSummaryDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public InspectionInstrumentDto Instrument { get; set; } = new();
    public string Source { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public DateTimeOffset SignalTimeUtc { get; set; }
    public string CanonicalMonth { get; set; } = string.Empty;
    public string DerivedStatus { get; set; } = string.Empty;
    public string? SetupName { get; set; }

    [JsonConverter(typeof(NullableDecimalStringJsonConverter))]
    public decimal? ReportedR { get; set; }

    [JsonConverter(typeof(NullableDecimalStringJsonConverter))]
    public decimal? NetR { get; set; }
}

public sealed class InspectionInstrumentDto
{
    public string Market { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
}

public sealed class TradeDetailDto
{
    public int SchemaVersion { get; set; }
    public string Contract { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Data { get; set; } = [];
}
