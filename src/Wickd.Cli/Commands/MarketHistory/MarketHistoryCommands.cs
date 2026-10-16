using Wickd.Cli.Commands.Journal;
using Wickd.Cli.Models;
using Wickd.Cli.Services;

namespace Wickd.Cli.Commands.MarketHistory;

/// <summary>Reads one exact, currently entitled snapshot without resolving or downloading market data.</summary>
public sealed class MarketSnapshotGetCommand(IApiClientFactory factory) : JournalCommand<IdentitySettings>(factory)
{
    protected override V2Operation Operation => V2Operation.MarketSnapshotGet;
}

/// <summary>Reads the API's explicit series coverage observations; absence remains unknown.</summary>
public sealed class MarketSeriesCoverageCommand(IApiClientFactory factory) : JournalCommand<IdentitySettings>(factory)
{
    protected override V2Operation Operation => V2Operation.MarketSeriesCoverage;
}
