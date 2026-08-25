using System.Text.Json;

namespace Wickd.Cli.Configuration;

public sealed class ConfigManager : IConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string GetActiveConfigPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var envPath = Environment.GetEnvironmentVariable("WICKD_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        var currentDirConfig = Path.Combine(Directory.GetCurrentDirectory(), "wickd.json");
        if (File.Exists(currentDirConfig))
        {
            return currentDirConfig;
        }

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var userConfigDir = Path.Combine(homeDir, ".wickd");
        return Path.Combine(userConfigDir, "config.json");
    }

    public WickdCliConfig LoadConfig(string? explicitPath = null)
    {
        var path = GetActiveConfigPath(explicitPath);
        if (!File.Exists(path))
        {
            return new WickdCliConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<WickdCliConfig>(json, JsonOptions);
            return config ?? new WickdCliConfig();
        }
        catch
        {
            return new WickdCliConfig();
        }
    }

    public void SaveConfig(WickdCliConfig config, string? explicitPath = null)
    {
        var path = GetActiveConfigPath(explicitPath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    public (bool created, string path) InitConfig(string? explicitPath = null, bool force = false)
    {
        var path = GetActiveConfigPath(explicitPath);
        if (File.Exists(path) && !force)
        {
            return (false, path);
        }

        var config = new WickdCliConfig();
        SaveConfig(config, path);
        return (true, path);
    }
}
