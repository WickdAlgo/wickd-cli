using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Wickd.Cli.Commands.Accounts;
using Wickd.Cli.Commands.Analyze;
using Wickd.Cli.Commands.Auth;
using Wickd.Cli.Commands.Backtest;
using Wickd.Cli.Commands.Config;
using Wickd.Cli.Commands.Fetch;
using Wickd.Cli.Commands.Manage;
using Wickd.Cli.Commands.Trades;
using Wickd.Cli.Common;
using Wickd.Cli.Configuration;
using Wickd.Cli.Rendering;
using Wickd.Cli.Services;

namespace Wickd.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();

        // Register Core Services
        services.AddSingleton<IConfigManager, ConfigManager>();
        services.AddSingleton<IConsoleRenderer, ConsoleRenderer>();
        services.AddSingleton<IApiClientFactory, ApiClientFactory>();

        // Register HttpClient
        services.AddHttpClient("WickdApi");

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("wickd");
            config.SetApplicationVersion("0.1.0");

            // fetch
            config.AddCommand<FetchCommand>("fetch")
                .WithDescription("Download and cache candles from supported exchange.");

            // backtest
            config.AddCommand<BacktestCommand>("backtest")
                .WithDescription("Replay cached market data through deterministic structure engine.");

            // analyze
            config.AddBranch("analyze", analyze =>
            {
                analyze.SetDescription("Perform quantitative and market structure analysis.");
                analyze.AddCommand<AnalyzeVwapCommand>("vwap")
                    .WithDescription("Compute session VWAPs, previous-close levels, and volume anomalies.");
            });

            // manage
            config.AddBranch("manage", manage =>
            {
                manage.SetDescription("Manage local/remote dataset aliases and backtest runs.");

                manage.AddBranch("datasets", datasets =>
                {
                    datasets.SetDescription("List or delete dataset aliases.");
                    datasets.AddCommand<ManageDatasetsListCommand>("list")
                        .WithDescription("List saved dataset aliases.");
                    datasets.AddCommand<ManageDatasetsDeleteCommand>("delete")
                        .WithDescription("Delete a dataset alias.");
                });

                manage.AddBranch("aliases", aliases =>
                {
                    aliases.SetDescription("Alias for 'manage datasets'.");
                    aliases.AddCommand<ManageDatasetsListCommand>("list")
                        .WithDescription("List saved dataset aliases.");
                    aliases.AddCommand<ManageDatasetsDeleteCommand>("delete")
                        .WithDescription("Delete a dataset alias.");
                });

                manage.AddBranch("runs", runs =>
                {
                    runs.SetDescription("List, inspect, or delete backtest runs.");
                    runs.AddCommand<ManageRunsListCommand>("list")
                        .WithDescription("List backtest runs.");
                    runs.AddCommand<ManageRunsGetCommand>("get")
                        .WithDescription("Inspect a specific backtest run.");
                    runs.AddCommand<ManageRunsDeleteCommand>("delete")
                        .WithDescription("Delete a backtest run.");
                });
            });

            // config
            config.AddBranch("config", cfg =>
            {
                cfg.SetDescription("View or modify Wickd CLI configuration.");
                cfg.AddCommand<ConfigInitCommand>("init")
                    .WithDescription("Create default user configuration file.");
                cfg.AddCommand<ConfigPathCommand>("path")
                    .WithDescription("Print active configuration file path.");
                cfg.AddCommand<ConfigGetCommand>("get")
                    .WithDescription("Get configuration value or view entire config.");
                cfg.AddCommand<ConfigSetCommand>("set")
                    .WithDescription("Set configuration key-value pair.");
            });

            // auth
            config.AddBranch("auth", auth =>
            {
                auth.SetDescription("Manage API authentication credentials.");
                auth.AddCommand<AuthLoginCommand>("login")
                    .WithDescription("Store API Bearer token in configuration.");
                auth.AddCommand<AuthStatusCommand>("status")
                    .WithDescription("Check API connectivity and authentication status.");
            });

            // trades
            config.AddBranch("trades", trades =>
            {
                trades.SetDescription("Inspect platform trade journal.");
                trades.AddCommand<TradesListCommand>("list")
                    .WithDescription("List trade journal summaries.");
                trades.AddCommand<TradesGetCommand>("get")
                    .WithDescription("Get detailed information for a specific trade.");
            });

            // accounts
            config.AddBranch("accounts", accounts =>
            {
                accounts.SetDescription("Inspect connected exchange accounts and risk.");
                accounts.AddCommand<AccountsListCommand>("list")
                    .WithDescription("List connected accounts.");
                accounts.AddCommand<AccountsRiskCommand>("risk")
                    .WithDescription("Evaluate account risk metrics.");
            });
        });

        return await app.RunAsync(args);
    }
}
