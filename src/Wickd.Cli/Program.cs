using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
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
    public static Task<int> Main(string[] args)
    {
        return CreateCommandApp().RunAsync(args);
    }

    /// <summary>
    /// Builds the production command app, optionally with a test registrar and console.
    /// </summary>
    /// <param name="registrar">Type registrar. Production services are used when omitted.</param>
    /// <param name="console">Optional Spectre console used to capture help in tests.</param>
    /// <returns>The configured command app.</returns>
    internal static CommandApp CreateCommandApp(ITypeRegistrar? registrar = null, IAnsiConsole? console = null)
    {
        if (registrar is null)
        {
            var services = new ServiceCollection();
            RegisterServices(services);
            registrar = new TypeRegistrar(services);
        }

        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            if (console is not null)
            {
                config.Settings.Console = console;
            }

            ConfigureCommands(config);
        });
        return app;
    }

    /// <summary>
    /// Registers the thin-client services used by the production command app.
    /// </summary>
    /// <param name="services">Service collection to populate.</param>
    internal static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IConfigManager, ConfigManager>();
        services.AddSingleton<IConsoleRenderer, ConsoleRenderer>();
        services.AddSingleton<IApiClientFactory, ApiClientFactory>();
        services.AddHttpClient("WickdApi");
    }

    /// <summary>
    /// Registers the public command map. <c>run</c> is the structure pipeline; <c>backtest</c> is not a command.
    /// </summary>
    /// <param name="config">Spectre command configurator.</param>
    internal static void ConfigureCommands(IConfigurator config)
    {
        config.SetApplicationName("wickd");
        config.SetApplicationVersion("0.1.0");

        config.AddCommand<FetchCommand>("fetch")
            .WithDescription("Download and cache candles from supported exchange.");

        config.AddCommand<BacktestCommand>("run")
            .WithDescription("Run the structure engine over a cached dataset or market range.");

        config.AddBranch("analyze", analyze =>
        {
            analyze.SetDescription("Perform quantitative and market structure analysis.");
            analyze.AddCommand<AnalyzeVwapCommand>("vwap")
                .WithDescription("Compute session VWAPs, previous-close levels, and volume anomalies.");
        });

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
                runs.SetDescription("List or inspect backtest runs.");
                runs.AddCommand<ManageRunsListCommand>("list")
                    .WithDescription("List backtest runs.");
                runs.AddCommand<ManageRunsGetCommand>("get")
                    .WithDescription("Inspect a specific backtest run.");
            });
        });

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

        config.AddBranch("auth", auth =>
        {
            auth.SetDescription("Manage API authentication credentials.");
            auth.AddCommand<AuthLoginCommand>("login")
                .WithDescription("Store API Bearer token in configuration.");
            auth.AddCommand<AuthStatusCommand>("status")
                .WithDescription("Check API connectivity and authentication status.");
        });

        config.AddBranch("trades", trades =>
        {
            trades.SetDescription("Inspect platform trade journal.");
            trades.AddCommand<TradesListCommand>("list")
                .WithDescription("List trade journal summaries.");
            trades.AddCommand<TradesGetCommand>("get")
                .WithDescription("Get detailed information for a specific trade.");
        });

        config.AddBranch("accounts", accounts =>
        {
            accounts.SetDescription("Inspect connected exchange accounts and risk.");
            accounts.AddCommand<AccountsListCommand>("list")
                .WithDescription("List connected accounts.");
            accounts.AddCommand<AccountsRiskCommand>("risk")
                .WithDescription("Evaluate account risk metrics.");
        });
    }
}
