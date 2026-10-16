using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using FluentAssertions;
using Wickd.Cli.Models;
using Xunit;

namespace Wickd.Cli.Tests;

public sealed class ContractPinTests
{
    private static readonly string[] ExpectedContracts =
    [
        "market-history.v2.schema.json",
        "market-coverage.v2.schema.json",
        "catalog-assets.v2.schema.json",
        "catalog-instruments.v2.schema.json",
        "instrument-spec-versions.v2.schema.json",
        "timeframe-definitions.v2.schema.json",
        "wallet-create-request.v2.schema.json",
        "wallet-list.v2.schema.json",
        "wallet-detail.v2.schema.json",
        "wallet-balance.v2.schema.json",
        "risk-preview-request.v2.schema.json",
        "risk-preview.v2.schema.json",
        "setup-disposition-request.v2.schema.json",
        "setup-note-request.v2.schema.json",
        "setup-accept-request.v2.schema.json",
        "setup-amend-request.v2.schema.json",
        "setup-attempt-create-request.v2.schema.json",
        "setup-attempt.v2.schema.json",
        "setup-attempt-list.v2.schema.json",
        "paper-fill-create-request.v2.schema.json",
        "paper-fill-correction-request.v2.schema.json",
        "paper-fill.v2.schema.json",
        "paper-fill-list.v2.schema.json",
        "journal-setup.v2.schema.json",
        "journal-setup-list.v2.schema.json",
        "account-risk.v1.schema.json",
        "accounts.v1.schema.json",
        "backtest-request.v1.schema.json",
        "backtest.v1.schema.json",
        "dataset-alias-request.v1.schema.json",
        "dataset-alias.v1.schema.json",
        "dataset-listing.v1.schema.json",
        "fetch.v1.schema.json",
        "inspection-dataset.v1.schema.json",
        "run-listing.v1.schema.json",
        "run-request.v1.schema.json",
        "run.v1.schema.json",
        "strategy-detail.v2.schema.json",
        "strategy-list-page.v2.schema.json",
        "strategy-version-list-page.v2.schema.json",
        "supported-instruments.v1.schema.json",
        "workspace-detail.v2.schema.json",
        "workspace-list.v2.schema.json",
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
        var run = JsonSerializer.Deserialize<BacktestResultDto>(
            File.ReadAllText(Path.Combine(samples, "run.v1.sample.json")), JsonOptions)!;
        var runRequest = JsonSerializer.Deserialize<StructureRunRequestDto>(
            File.ReadAllText(Path.Combine(samples, "run-request.v1.sample.json")), JsonOptions)!;
        var vwap = JsonSerializer.Deserialize<VwapAnalysisResultDto>(
            File.ReadAllText(Path.Combine(samples, "vwap-analysis.v1.sample.json")), JsonOptions)!;

        (fetch.SchemaVersion, fetch.Contract).Should().Be((1, "fetch"));
        (backtest.SchemaVersion, backtest.Contract).Should().Be((1, "backtest"));
        (run.SchemaVersion, run.Contract).Should().Be((1, "run"));
        runRequest.RunId.Should().Be("run-sample");
        runRequest.Dataset.Timeframe.Should().Be("5m");
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

    [Theory]
    [InlineData("setup-attempt-create-request.v2.schema.json", "0ecd2dce76110ec38652636f591d8ae4601300ede0e76cac17750bfefd1c9079")]
    [InlineData("setup-attempt.v2.schema.json", "2435ba41f2134115a1a023c7c44ea76ca034d7929a8cd82bb8ed9bd9836117e6")]
    [InlineData("setup-attempt-list.v2.schema.json", "30e939d2844eaa4e151c7fd6d1ee8c011952220843310eb7b0d5af596521f66b")]
    [InlineData("samples/setup-attempt-create-request.v2.sample.json", "7e1dc0f8b5fff5fd131a497116aefbfe1f76fd0573559a77ad67abffd4f287f9")]
    [InlineData("samples/setup-attempt.v2.sample.json", "8ea9bfe31c2c362c54d017820b80418656d2791771a876ece3413d7a761db341")]
    [InlineData("samples/setup-attempt-list.v2.sample.json", "8c14f8bc32c643e04cc11b5a2a75b5fe515ef63cac96c9241ba09291c7410180")]
    public void SetupAttemptArtifactsAreExactProducerBytes(string relative, string expected)
    {
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(ContractsRoot(), relative))))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("paper-fill-create-request.v2.schema.json", "24b108a22e8a693ec9f2c317b02560bb823a47bf85e86b498552bea0eae15330")]
    [InlineData("paper-fill-correction-request.v2.schema.json", "b6692e6faef6ff68e9a283b7a78bce3782ec280f2337d75202708e0fb89c3ab4")]
    [InlineData("paper-fill.v2.schema.json", "f07430a5235b764e40fa6f1013a24252132e00cc6f1dd29e1d270fe53e30a463")]
    [InlineData("paper-fill-list.v2.schema.json", "7034b85b1d98e07c2afc21154a7d486b3dac9beadc649cabada568ea512ad52d")]
    [InlineData("samples/paper-fill-create-request.v2.sample.json", "1cb67bd6a25fd12f8bf52617e615edc2210d91e5d4f3c7365e99e4390893251a")]
    [InlineData("samples/paper-fill-correction-request.v2.sample.json", "ad6cefa16b24e62d8cc7eefce8a349155612993b11b387cac60221dc20fc55bb")]
    [InlineData("samples/paper-fill.v2.sample.json", "a225cc62a14ab97ac40389fdfc4f824853d7564006dea47dc1a273c818b5e466")]
    [InlineData("samples/paper-fill-list.v2.sample.json", "882d38a8164effb6c4f3afd080fc668ad170336ee2665213cba416d0aded62eb")]
    public void PaperFillArtifactsAreExactProducerBytes(string relative, string expected)
    {
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(ContractsRoot(), relative))))
            .Should().Be(expected);
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
