using FluentAssertions;
using Spectre.Console;
using Wickd.Cli.Models;
using Wickd.Cli.Rendering;

namespace Wickd.Cli.Tests;

public sealed class ConsoleRendererTests
{
    private static readonly object ConsoleLock = new();

    [Fact]
    public void TradeTableEscapesMarkupInSetupNames()
    {
        lock (ConsoleLock)
        {
            var originalConsole = AnsiConsole.Console;
            using var writer = new StringWriter();
            try
            {
                AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Ansi = AnsiSupport.No,
                    Interactive = InteractionSupport.No,
                    Out = new AnsiConsoleOutput(writer)
                });

                new ConsoleRenderer().RenderTrades(
                [
                    new TradeSummaryDto
                    {
                        Id = "trade-1",
                        Source = "journal",
                        Instrument = new InspectionInstrumentDto { Market = "BTC/USDT" },
                        Direction = "long",
                        SetupName = "Sweep [v2]",
                        DerivedStatus = "closed"
                    }
                ]);

                writer.ToString().Should().Contain("Sweep").And.Contain("[v2]");
            }
            finally
            {
                AnsiConsole.Console = originalConsole;
            }
        }
    }
}
