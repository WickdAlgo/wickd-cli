namespace Wickd.Cli.Configuration;

public interface IConfigManager
{
    string GetActiveConfigPath(string? explicitPath = null);
    WickdCliConfig LoadConfig(string? explicitPath = null);
    void SaveConfig(WickdCliConfig config, string? explicitPath = null);
    (bool created, string path) InitConfig(string? explicitPath = null, bool force = false);
}
