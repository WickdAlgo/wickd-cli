using System.Text.Json.Serialization;

namespace Wickd.Cli.Configuration;

public sealed class WickdCliConfig
{
    [JsonPropertyName("apiUrl")]
    public string ApiUrl { get; set; } = "http://localhost:5081";

    [JsonPropertyName("apiToken")]
    public string? ApiToken { get; set; }

    [JsonPropertyName("defaultExchange")]
    public string DefaultExchange { get; set; } = "binance";

    [JsonPropertyName("defaultMarket")]
    public string DefaultMarket { get; set; } = "BTC_USDT_PERP";

    [JsonPropertyName("defaultTimeframe")]
    public string DefaultTimeframe { get; set; } = "4h";

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 60;

    [JsonPropertyName("structure")]
    public StructureConfig Structure { get; set; } = new();

    [JsonPropertyName("vwap")]
    public VwapConfig Vwap { get; set; } = new();
}

public sealed class StructureConfig
{
    [JsonPropertyName("pivotStrength")]
    public int PivotStrength { get; set; } = 2;
}

public sealed class VwapConfig
{
    [JsonPropertyName("enabledPeriods")]
    public List<string> EnabledPeriods { get; set; } = ["daily", "weekly"];

    [JsonPropertyName("previousLevelPeriods")]
    public List<string> PreviousLevelPeriods { get; set; } = ["daily", "weekly"];

    [JsonPropertyName("volumeLength")]
    public int VolumeLength { get; set; } = 20;

    [JsonPropertyName("mediumThreshold")]
    public decimal MediumThreshold { get; set; } = 1.5m;

    [JsonPropertyName("largeThreshold")]
    public decimal LargeThreshold { get; set; } = 2.5m;

    [JsonPropertyName("lowVolumeThreshold")]
    public decimal LowVolumeThreshold { get; set; } = -1m;

    [JsonPropertyName("showLowVolume")]
    public bool ShowLowVolume { get; set; } = true;
}
