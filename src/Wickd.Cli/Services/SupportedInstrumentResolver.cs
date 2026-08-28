using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

internal static class SupportedInstrumentResolver
{
    internal static async Task<SupportedInstrumentDto> ResolveAsync(
        IWickdApiClient client,
        string marketId,
        string exchangeId,
        string timeframe,
        CancellationToken cancellationToken)
    {
        var supported = await client.GetSupportedInstrumentsAsync(cancellationToken);
        if (!supported.Timeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"API does not advertise timeframe '{timeframe}'.");
        }

        return supported.Instruments.SingleOrDefault(instrument =>
                instrument.MarketId.Equals(marketId, StringComparison.OrdinalIgnoreCase)
                && instrument.ExchangeId.Equals(exchangeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"API does not advertise market '{marketId}' on exchange '{exchangeId}'.");
    }
}
