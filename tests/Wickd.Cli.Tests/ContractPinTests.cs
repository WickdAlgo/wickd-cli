using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Wickd.Cli.Models;
using Xunit;

namespace Wickd.Cli.Tests;

public sealed class ContractPinTests
{
    private static readonly string[] ExpectedContracts =
    [
        "account-risk.v1.schema.json",
        "accounts.v1.schema.json",
        "backtest-request.v1.schema.json",
        "backtest.v1.schema.json",
        "dataset-alias-request.v1.schema.json",
        "dataset-alias.v1.schema.json",
        "fetch.v1.schema.json",
        "inspection-dataset.v1.schema.json",
        "run-listing.v1.schema.json",
        "supported-instruments.v1.schema.json",
        "trade-detail.v1.schema.json",
        "trade-summary.v1.schema.json",
        "vwap-analysis-request.v1.schema.json",
        "vwap-analysis.v1.schema.json"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TheVendoredContractSetIsExact()
    {
        var actual = Directory.GetFiles(ContractsRoot(), "*.schema.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        actual.Should().Equal(ExpectedContracts.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CanonicalResultSamplesDeserializeThroughThePublicClientModels()
    {
        var samples = Path.Combine(ContractsRoot(), "samples");

        var fetch = JsonSerializer.Deserialize<FetchResultDto>(
            File.ReadAllText(Path.Combine(samples, "fetch.v1.sample.json")), JsonOptions)!;
        var backtest = JsonSerializer.Deserialize<BacktestResultDto>(
            File.ReadAllText(Path.Combine(samples, "backtest.v1.sample.json")), JsonOptions)!;
        var vwap = JsonSerializer.Deserialize<VwapAnalysisResultDto>(
            File.ReadAllText(Path.Combine(samples, "vwap-analysis.v1.sample.json")), JsonOptions)!;

        (fetch.SchemaVersion, fetch.Contract).Should().Be((1, "fetch"));
        (backtest.SchemaVersion, backtest.Contract).Should().Be((1, "backtest"));
        (vwap.SchemaVersion, vwap.Contract).Should().Be((1, "vwap-analysis"));
        vwap.Series.Should().NotBeEmpty();
        vwap.CandleCount.Should().BeGreaterThan(0);
        vwap.FromUtc.Should().BeBefore(vwap.ToUtc);
    }

    [Fact]
    public void FetchRequestMatchesTheVendoredRequiredWireKeys()
    {
        var schema = JsonNode.Parse(File.ReadAllText(
            Path.Combine(ContractsRoot(), "fetch.v1.schema.json")))!.AsObject();
        var resultKeys = schema["required"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);

        resultKeys.Should().Contain(["exchangeSymbol", "candleCount", "cacheHit"]);
        resultKeys.Should().NotContain(["alias", "force", "candlesFetched", "cachePath"]);
    }

    [Fact]
    public void ComputeSettingsArePartOfTheVendoredRequestContracts()
    {
        var backtest = JsonNode.Parse(File.ReadAllText(
            Path.Combine(ContractsRoot(), "backtest-request.v1.schema.json")))!.AsObject();
        var vwap = JsonNode.Parse(File.ReadAllText(
            Path.Combine(ContractsRoot(), "vwap-analysis-request.v1.schema.json")))!.AsObject();

        backtest["properties"]!["pivotStrength"]!["minimum"]!.GetValue<int>().Should().Be(1);
        var settings = vwap["$defs"]!["VwapAnalysisSettings"]!;
        settings["required"]!.AsArray().Select(item => item!.GetValue<string>())
            .Should().Contain(["enabledPeriods", "previousLevelPeriods", "volumeLength"]);
    }

    private static string ContractsRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Wickd.Cli.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root."),
            "contracts");
    }
}
