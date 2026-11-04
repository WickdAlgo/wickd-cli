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
using Wickd.Cli.Commands.MarketHistory;
using Wickd.Cli.Commands.Journal;
using Wickd.Cli.Commands.StrategyContext;
using Wickd.Cli.Commands.Strategies;
using Wickd.Cli.Commands.Trades;
using Wickd.Cli.Commands.Workspaces;
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

            manage.AddBranch("strategies", strategies =>
            {
                strategies.SetDescription("Browse the selected workspace strategy book.");
                strategies.AddCommand<StrategyListCommand>("list").WithDescription("List strategies in the selected workspace.");
                strategies.AddCommand<StrategyGetCommand>("get").WithDescription("Get a strategy and its metadata.");
            });
        });

        config.AddBranch("context", context =>
        {
            context.SetDescription("Read and retain human session definitions and market-condition labels.");
            context.AddBranch("sessions", sessions =>
            {
                sessions.SetDescription("Create and inspect immutable session-definition versions.");
                sessions.AddCommand<SessionDefinitionCreateCommand>("create");
                sessions.AddCommand<SessionDefinitionVersionCreateCommand>("revise");
                sessions.AddCommand<SessionDefinitionVersionListCommand>("list");
                sessions.AddCommand<SessionDefinitionVersionGetCommand>("get");
            });
            context.AddBranch("conditions", conditions =>
            {
                conditions.SetDescription("Create and inspect immutable market-condition label revisions.");
                conditions.AddCommand<MarketConditionCreateCommand>("create");
                conditions.AddCommand<MarketConditionRevisionCreateCommand>("revise");
                conditions.AddCommand<MarketConditionRevisionListCommand>("list");
                conditions.AddCommand<MarketConditionRevisionGetCommand>("get");
            });
        });

        config.AddBranch("strategy", strategy =>
        {
            strategy.SetDescription("Read human strategy scopes and owner-private retained illustrations.");
            strategy.AddBranch("example", examples =>
            {
                examples.AddCommand<StrategyExampleCreateCommand>("create").WithDescription("Retain an owner-private human illustration using exact existing evidence.");
                examples.AddCommand<StrategyExampleListCommand>("list").WithDescription("List exact retained illustration references; decisionOnly is the default.");
                examples.AddCommand<StrategyExampleGetCommand>("get").WithDescription("Read an exact illustration reference at its own receipt.");
                examples.AddCommand<StrategyExampleEvidenceCommand>("evidence").WithDescription("Read the server-owned decision evidence for an exact decisionOnly reference.");
            });
            strategy.AddBranch("scope", scope =>
            {
                scope.AddCommand<StrategyScopeGetCommand>("get").WithDescription("Read instrument/timeframe scope at current or frozen publication.");
                scope.AddCommand<StrategyScopeUpdateCommand>("update").WithDescription("Replace instrument/timeframe scope on a draft using its current ETag.");
                scope.AddCommand<StrategyScopeListCommand>("history").WithDescription("List retained scope versions for a strategy.");
            });
            strategy.AddBranch("context", context =>
            {
                context.AddCommand<StrategyContextScopeGetCommand>("get").WithDescription("Read session/condition suitability at current or frozen publication.");
                context.AddCommand<StrategyContextScopeUpdateCommand>("update").WithDescription("Replace contextual suitability on a draft using its shared draft ETag.");
            });
        });

        config.AddBranch("history", history =>
        {
            history.SetDescription("Read retained market evidence authorized for the selected workspace.");
            history.AddBranch("snapshot", snapshots =>
            {
                snapshots.AddCommand<MarketSnapshotGetCommand>("get")
                    .WithDescription("Read an exact immutable snapshot, its series provenance and decimal-string candles.");
            });
            history.AddBranch("series", series =>
            {
                series.AddCommand<MarketSeriesCoverageCommand>("coverage")
                    .WithDescription("Read checked coverage and explicit unavailable-history limitations for a series.");
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

        config.AddBranch("workspace", workspace =>
        {
            workspace.SetDescription("Discover available workspaces and select the local v2 workspace context.");
            workspace.AddCommand<WorkspaceListCommand>("list").WithDescription("List workspaces available to the configured user session.");
            workspace.AddCommand<WorkspaceUseCommand>("use").WithDescription("Select a workspace in local CLI configuration.");
        });

        config.AddBranch("catalog", catalog =>
        {
            catalog.SetDescription("Read the authenticated global instrument and asset catalog.");
            catalog.AddCommand<CatalogAssetsCommand>("assets");
            catalog.AddCommand<CatalogInstrumentsCommand>("instruments");
            catalog.AddCommand<CatalogSpecsCommand>("specs");
            catalog.AddCommand<CatalogTimeframesCommand>("timeframes");
        });
        config.AddBranch("wallet", wallet =>
        {
            wallet.SetDescription("Manage paper/backtest wallet identities, retained opening basis and unassigned paper fill evidence.");
            wallet.AddCommand<WalletCreateCommand>("create");
            wallet.AddCommand<WalletListCommand>("list");
            wallet.AddCommand<WalletGetCommand>("get");
            wallet.AddCommand<WalletBalanceCommand>("balance");
            wallet.AddBranch("history", history =>
            {
                history.SetDescription("Record and inspect user-declared paper trading history coverage.");
                history.AddCommand<PaperHistoryCreateCommand>("create").WithDescription("Record the declared opening and manual-fill coverage for one wallet instrument; no imported history or opening cash is created.");
                history.AddCommand<PaperHistoryListCommand>("list").WithDescription("List one frozen page of paper history attestations for one wallet instrument.");
                history.AddCommand<PaperHistoryGetCommand>("get").WithDescription("Read one paper history attestation, optionally at a frozen cursor.");
                history.AddCommand<PaperHistoryCorrectCommand>("correct").WithDescription("Append a paper history correction with the current strong wallet/instrument history ETag.");
            });
            wallet.AddBranch("accounting", accounting =>
            {
                accounting.SetDescription("Create and inspect retained paper FIFO projection receipts from captured fills and explicit history declarations.");
                accounting.AddCommand<WalletAccountingCreateCommand>("create").WithDescription("Request a retained paper FIFO projection receipt for one wallet instrument; the API derives it from current evidence.");
                accounting.AddCommand<WalletAccountingListCommand>("list").WithDescription("List one frozen page of retained accounting receipts for one wallet instrument.");
                accounting.AddCommand<WalletAccountingGetCommand>("get").WithDescription("Read one retained accounting receipt, optionally at a frozen cursor.");
            });
            wallet.AddBranch("allocations", allocations =>
            {
                allocations.SetDescription("Record and inspect retained paper activity attribution to strategy attempts.");
                allocations.AddCommand<PaperAllocationListCommand>("list").WithDescription("List frozen paper activity allocation revisions for one wallet instrument.");
                allocations.AddCommand<PaperAllocationGetCommand>("get").WithDescription("Read one allocation revision, optionally at a frozen cursor.");
                allocations.AddCommand<PaperAllocationAssignCommand>("assign").WithDescription("Append a complete paper activity allocation view with an explicit idempotency key and current strong ETag.");
            });
            wallet.AddBranch("cost-attributions", costs =>
            {
                costs.SetDescription("Assign and inspect retained paper cost ownership by strategy attempt.");
                costs.AddCommand<CostAttributionListCommand>("list").WithDescription("List frozen attribution revisions for one wallet root cost.");
                costs.AddCommand<CostAttributionGetCommand>("get").WithDescription("Read an attribution revision, optionally at a frozen cursor.");
                costs.AddCommand<CostAttributionAssignCommand>("assign").WithDescription("Append a full cost attribution view with an explicit key and current strong ETag.");
            });
            wallet.AddBranch("costs", costs =>
            {
                costs.SetDescription("Record and inspect user-declared paper wallet costs; amount and cost coverage remain server-owned.");
                costs.AddCommand<PaperCostCreateCommand>("create").WithDescription("Record one manual cost with an explicit idempotency key; amount may be null when unknown.");
                costs.AddCommand<PaperCostListCommand>("list").WithDescription("List one frozen page of retained paper wallet cost revisions.");
                costs.AddCommand<PaperCostGetCommand>("get").WithDescription("Read one cost revision, optionally at a frozen cursor.");
                costs.AddCommand<PaperCostCorrectCommand>("correct").WithDescription("Append a cost correction with the current strong wallet cost ETag and an explicit idempotency key.");
            });
            wallet.AddBranch("fills", fills =>
            {
                fills.SetDescription("Capture and inspect native paper fills. These records are not executions or accounting.");
                fills.AddCommand<PaperFillCreateCommand>("create").WithDescription("Record one explicit paper fill; no order is sent and no allocation is created.");
                fills.AddCommand<PaperFillListCommand>("list").WithDescription("List one frozen page of unassigned paper fills for a wallet.");
                fills.AddCommand<PaperFillGetCommand>("get").WithDescription("Read a paper fill by wallet and fill identity, optionally at a frozen cursor.");
                fills.AddCommand<PaperFillCorrectCommand>("correct").WithDescription("Append a correction with an explicit idempotency key and the current strong wallet fills ETag.");
            });
        });
        config.AddBranch("risk", risk =>
        {
            risk.SetDescription("Request server-calculated sizing for a complete plan.");
            risk.AddCommand<RiskPreviewCommand>("preview");
        });
        config.AddBranch("setup", setup =>
        {
            setup.SetDescription("Accept and inspect wallet-bound journal setups.");
            setup.AddCommand<SetupAcceptCommand>("accept");
            setup.AddCommand<SetupListCommand>("list");
            setup.AddCommand<SetupGetCommand>("get").WithDescription("Read current detail or frozen detail with --cursor from list snapshotCursor.");
            setup.AddCommand<SetupAmendCommand>("amend").WithDescription("Append a complete plan revision from a new preview; requires current ETag.");
            setup.AddCommand<SetupSkipCommand>("skip").WithDescription("Record a skipped opportunity using an explicit markSkipped request and current ETag.");
            setup.AddCommand<SetupMissedCommand>("missed").WithDescription("Record a missed opportunity using an explicit markMissed request and current ETag.");
            setup.AddCommand<SetupCancelCommand>("cancel").WithDescription("Record cancellation using an explicit cancel request and current ETag.");
            setup.AddBranch("notes", notes =>
            {
                notes.SetDescription("Append personal/observation notes and retain their revision history.");
                notes.AddCommand<SetupNoteAppendCommand>("append").WithDescription("Append a note with supersedesId null; requires current setup ETag.");
                notes.AddCommand<SetupNoteReviseCommand>("revise").WithDescription("Revise the latest note using its supersedesId; requires current setup ETag.");
            });
            setup.AddBranch("attempts", attempts =>
            {
                attempts.SetDescription("Create and inspect native paper/backtest attempts for one setup.");
                attempts.AddCommand<SetupAttemptCreateCommand>("create").WithDescription("Create one attempt from an accepted plan; requires current setup ETag and an explicit idempotency key.");
                attempts.AddCommand<SetupAttemptListCommand>("list").WithDescription("List attempts for this setup; --cursor pages are scoped to the selected workspace.");
                attempts.AddCommand<SetupAttemptGetCommand>("get").WithDescription("Read one attempt for this setup, optionally frozen with --cursor from setup list or attempts list snapshotCursor.");
            });
            setup.AddBranch("context", context =>
            {
                context.SetDescription("Read and append retained human setup context observations.");
                context.AddCommand<SetupContextGetCommand>("get").WithDescription("Read the latest context at current or frozen publication, optionally filtered by session version and condition tag.");
                context.AddCommand<SetupContextObservationCreateCommand>("append").WithDescription("Append a complete context observation with its independent context ETag.");
                context.AddCommand<SetupContextObservationListCommand>("history").WithDescription("List the frozen, append-only context observation history.");
            });
            setup.AddBranch("review", review =>
            {
                review.SetDescription("Append and inspect immutable human reviews of accepted setups.");
                review.AddCommand<SetupReviewGetCommand>("get").WithDescription("Read the latest review or explicit noReview state with its independent review ETag.");
                review.AddCommand<SetupReviewAppendCommand>("append").WithDescription("Append a complete review snapshot with the current review ETag and stable idempotency key.");
                review.AddCommand<SetupReviewRevisionCommand>("revision").WithDescription("Read one exact retained review revision, optionally frozen with a review-stream cursor.");
                review.AddCommand<SetupReviewHistoryCommand>("history").WithDescription("List ascending immutable review revisions in a frozen review-stream page.");
            });
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
