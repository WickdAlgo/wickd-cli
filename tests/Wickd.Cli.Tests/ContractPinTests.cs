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
        "paper-history-create-request.v2.schema.json",
        "paper-history-correction-request.v2.schema.json",
        "paper-history.v2.schema.json",
        "paper-history-list.v2.schema.json",
        "wallet-accounting-create-request.v2.schema.json",
        "wallet-accounting.v2.schema.json",
        "wallet-accounting-list.v2.schema.json",
        "paper-allocation-request.v2.schema.json",
        "paper-allocation.v2.schema.json",
        "paper-allocation-list.v2.schema.json",
        "paper-cost-create-request.v2.schema.json",
        "paper-cost-correction-request.v2.schema.json",
        "paper-cost.v2.schema.json",
        "paper-cost-list.v2.schema.json",
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

    [Theory]
    [InlineData("paper-history-create-request.v2.schema.json", "9c9a65163f3a684053cfe33a97fa4370de316867690aa60c0c5f050b1f467265")]
    [InlineData("paper-history-correction-request.v2.schema.json", "9425a195f9a808116cbd295e6fd69bc2654cf5f2d2670f5cf380d0249f7ad8db")]
    [InlineData("paper-history.v2.schema.json", "b8d65b41eb6e7bfddc5f0aca1c0e0ead20648c0ff473c1ab6ff9dced26dd2527")]
    [InlineData("paper-history-list.v2.schema.json", "a5487b0bef616c62fa23d44c8b79995f25c570a6f69debb9e705d5b43f0c7920")]
    [InlineData("wallet-accounting-create-request.v2.schema.json", "b5dc5f14f2cd12fdc7a3839c983eae71b895982ebdf723eb8c5f68791dd8b2b3")]
    [InlineData("wallet-accounting.v2.schema.json", "932de88746e262aecfcc2c42397bead18cd80ff3abf2092febe260b67bea3450")]
    [InlineData("wallet-accounting-list.v2.schema.json", "5c6f5d265d815a8beb1ee5956a2b4421d18bfe6d4c3bd48163b03ad0fa5ef61b")]
    [InlineData("samples/paper-history-create-request.v2.sample.json", "6699eaa04352b454353c2e77f6624592cbceac7c7ff7327e30fd2a5c104974e9")]
    [InlineData("samples/paper-history-correction-request.v2.sample.json", "806694b4d9ff4b101e59d5d567fd669b6f15d96596a766adc2ac0d8cc8141f5e")]
    [InlineData("samples/paper-history.v2.sample.json", "1f5c113f11ca9ab21ff25a1b0ca1bcf706b1ac5bb1275cf17d8704febfdea243")]
    [InlineData("samples/paper-history-list.v2.sample.json", "e148be9617219a56ca803cdfb3937c5c57481c5e8dbbad4fabeeb408a09ee47c")]
    [InlineData("samples/wallet-accounting-create-request.v2.sample.json", "4a4989afbb1df8ea906d9eafbaac7388a59d844db97efea2b5b32a908407c7f6")]
    [InlineData("samples/wallet-accounting.v2.sample.json", "bce21aa786a090cce3269921cefd914b54c7b3dbf0a05916a20f29b8cb26dd21")]
    [InlineData("samples/wallet-accounting-list.v2.sample.json", "9d4b6c243e999278503f80786cf85955e7b061412fc5dd031c8986913dbcf90c")]
    public void PaperHistoryAndWalletAccountingArtifactsAreExactProducerBytes(string relative, string expected)
    {
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(ContractsRoot(), relative))))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("paper-allocation-request.v2.schema.json", "cce47d6ff4bb7931d68efc46894bbe325cbc28fa40f866903b67fa2e0eba5782")]
    [InlineData("paper-allocation.v2.schema.json", "bcb68e82d4d60bf26022bf0f07762c1de6ac30e529705b50b310337eb4f1099a")]
    [InlineData("paper-allocation-list.v2.schema.json", "4eaa693d5e0a74ae4bbf401bebaca19cc153252525e48fcda7961e35fb753247")]
    [InlineData("samples/paper-allocation-request.v2.sample.json", "1b9a7756f1a21bad45ab728e0b085a9b02760cd0cd061de1484c50ecba9dab59")]
    [InlineData("samples/paper-allocation.v2.sample.json", "ee6a587f8f929cb67b4e2a447a8140ef1dc5857fca47b0b2ca1ea5f2be351b8a")]
    [InlineData("samples/paper-allocation-list.v2.sample.json", "48cca7dab0f93a390464d1dca0580d9694ec6e437c437317ebf71a48fe47da37")]
    public void PaperAllocationArtifactsAreExactProducerBytes(string relative, string expected)
    {
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(ContractsRoot(), relative))))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("paper-cost-create-request.v2.schema.json", "413dfd211069c7bba7ed81171c20e038732fb504a87e35b525d1eae2f66bce90")]
    [InlineData("paper-cost-correction-request.v2.schema.json", "53ef0583432a808492341b2783822857b4145b69dff8c42e340bd42ac2e795fb")]
    [InlineData("paper-cost.v2.schema.json", "7a71cd767b177da7aac43e4cc1eb2cac196d0abcae0f0814813ef7aa35238b66")]
    [InlineData("paper-cost-list.v2.schema.json", "37e96fb83181c3b8a5b4d648f868d1dd7127729f0136a1bb791f8700bb2cd912")]
    [InlineData("samples/paper-cost-create-request.v2.sample.json", "610ecc2a5d0f28f3f5854ae93b0eaf688b86be81c71c50f8e3522de2b4d1e3f1")]
    [InlineData("samples/paper-cost-correction-request.v2.sample.json", "347adcceb7e96996db7695b50fa44159e2ccc849ae0d36e559fe17c46ef81d05")]
    [InlineData("samples/paper-cost.v2.sample.json", "32f96e311c72b1110b4359276d59c3f920d0a578a41c2396ad066634f3e0c3ed")]
    [InlineData("samples/paper-cost-list.v2.sample.json", "3ea88be15ab5bc22c2e9757ff85a79736c2b5143560b1ae3519ff68784cb209d")]
    public void PaperCostArtifactsAreExactProducerBytes(string relative, string expected)
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
