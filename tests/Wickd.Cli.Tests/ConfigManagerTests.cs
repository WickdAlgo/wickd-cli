using FluentAssertions;
using Wickd.Cli.Configuration;
using Xunit;

namespace Wickd.Cli.Tests;

public class ConfigManagerTests : IDisposable
{
    private readonly string _tempDirectory;

    public ConfigManagerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "wickd_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void GetActiveConfigPath_WithExplicitPath_ReturnsFullPath()
    {
        var manager = new ConfigManager();
        var customPath = Path.Combine(_tempDirectory, "my-config.json");

        var resolved = manager.GetActiveConfigPath(customPath);

        resolved.Should().Be(Path.GetFullPath(customPath));
    }

    [Fact]
    public void LoadConfig_WhenFileDoesNotExist_ReturnsDefaultConfig()
    {
        var manager = new ConfigManager();
        var nonExistentPath = Path.Combine(_tempDirectory, "does_not_exist.json");

        var config = manager.LoadConfig(nonExistentPath);

        config.Should().NotBeNull();
        config.ApiUrl.Should().Be("http://localhost:5080");
        config.DefaultMarket.Should().Be("BTC_USDT_PERP");
        config.DefaultTimeframe.Should().Be("4h");
        config.Structure.PivotStrength.Should().Be(2);
    }

    [Fact]
    public void SaveAndLoadConfig_RoundTripsCorrectly()
    {
        var manager = new ConfigManager();
        var testPath = Path.Combine(_tempDirectory, "subfolder", "config.json");

        var config = new WickdCliConfig
        {
            ApiUrl = "https://custom.wickd.io",
            ApiToken = "test-token-12345",
            DefaultMarket = "ETH_USDT_PERP",
            DefaultTimeframe = "1h",
            Structure = new StructureConfig { PivotStrength = 5 },
            Vwap = new VwapConfig { VolumeLength = 30 }
        };

        manager.SaveConfig(config, testPath);
        var loaded = manager.LoadConfig(testPath);

        loaded.Should().NotBeNull();
        loaded.ApiUrl.Should().Be("https://custom.wickd.io");
        loaded.ApiToken.Should().Be("test-token-12345");
        loaded.DefaultMarket.Should().Be("ETH_USDT_PERP");
        loaded.DefaultTimeframe.Should().Be("1h");
        loaded.Structure.PivotStrength.Should().Be(5);
        loaded.Vwap.VolumeLength.Should().Be(30);
    }

    [Fact]
    public void InitConfig_CreatesFile_AndReportsCorrectly()
    {
        var manager = new ConfigManager();
        var testPath = Path.Combine(_tempDirectory, "wickd_init.json");

        var (created1, path1) = manager.InitConfig(testPath, force: false);
        created1.Should().BeTrue();
        File.Exists(path1).Should().BeTrue();

        // Second time without force should report false
        var (created2, path2) = manager.InitConfig(testPath, force: false);
        created2.Should().BeFalse();
        path2.Should().Be(path1);

        // Third time with force should succeed
        var (created3, path3) = manager.InitConfig(testPath, force: true);
        created3.Should().BeTrue();
        path3.Should().Be(path1);
    }
}
