using System.ComponentModel;
using Spectre.Console.Cli;

namespace Wickd.Cli.Commands.Settings;

public class GlobalCommandSettings : CommandSettings
{
    [Description("Path to custom configuration file.")]
    [CommandOption("-c|--config <PATH>")]
    public string? ConfigPath { get; init; }

    [Description("Override target WickdAlgo API URL.")]
    [CommandOption("--api-url <URL>")]
    public string? ApiUrl { get; init; }

    [Description("Override API authentication Bearer token.")]
    [CommandOption("--token <TOKEN>")]
    public string? Token { get; init; }

    [Description("Output response data as raw JSON.")]
    [CommandOption("--json")]
    public bool Json { get; init; }
}
