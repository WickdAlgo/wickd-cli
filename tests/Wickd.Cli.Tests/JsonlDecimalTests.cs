using System.Text.Json;
using FluentAssertions;
using Wickd.Cli.Commands.Analyze;
using Wickd.Cli.Models;

namespace Wickd.Cli.Tests;

public sealed class JsonlDecimalTests
{
    [Fact]
    public void AnonymousVwapExportWritesInvariantDecimalStrings()
    {
        var json = JsonSerializer.Serialize(
            new
            {
                kind = "point",
                runningVwap = 101.25m,
                previousClose = (decimal?)null,
                price = 100.5m,
                score = (decimal?)2.5m
            },
            AnalyzeVwapCommand.JsonLineOptions);

        json.Should().Contain("\"runningVwap\":\"101.25\"");
        json.Should().Contain("\"price\":\"100.5\"");
        json.Should().Contain("\"score\":\"2.5\"");
        json.Should().Contain("\"previousClose\":null");
        json.Should().NotContain("101.25,");
        json.Should().NotContain(":101.25");
    }
}
