using System.Net.Http.Headers;
using System.Text.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient
{
    public async Task<V2Response> SendV2Async(V2Call call, CancellationToken cancellationToken = default)
    {
        var (path, responseContract, requestContract) = call.Operation switch
        {
            V2Operation.CatalogAssets => ("catalog/assets", "catalog-assets", (string?)null),
            V2Operation.CatalogInstruments => ("catalog/instruments", "catalog-instruments", null),
            V2Operation.CatalogSpecs => ("catalog/instrument-spec-versions", "instrument-spec-versions", null),
            V2Operation.CatalogTimeframes => ("catalog/timeframes", "timeframe-definitions", null),
            V2Operation.WalletCreate => ("wallets", "wallet-detail", "wallet-create-request"),
            V2Operation.WalletList => ("wallets", "wallet-list", null),
            V2Operation.WalletGet => ($"wallets/{Segment(call.Id)}", "wallet-detail", null),
            V2Operation.WalletBalance => ($"wallets/{Segment(call.Id)}/balance", "wallet-balance", null),
            V2Operation.RiskPreview => ("risk/previews", "risk-preview", "risk-preview-request"),
            V2Operation.SetupAccept => ("setups", "journal-setup", "setup-accept-request"),
            V2Operation.SetupList => ("setups", "journal-setup-list", null),
            V2Operation.SetupGet => ($"setups/{Segment(call.Id)}", "journal-setup", null),
            V2Operation.SetupAmend => ($"setups/{Segment(call.Id)}/plan-revisions", "journal-setup", "setup-amend-request"),
            _ => throw new ArgumentException("Unknown v2 operation.")
        };
        RequireV2Credentials(requireWorkspace: !path.StartsWith("catalog/", StringComparison.Ordinal));
        if (call.Operation is V2Operation.WalletList or V2Operation.SetupList)
        {
            if (call.Limit is < 1 or > 100) throw new ArgumentException("Page limit must be between 1 and 100.");
            if (call.Cursor is not null && (string.IsNullOrWhiteSpace(call.Cursor) || call.Cursor.Any(char.IsControl)))
                throw new ArgumentException("Cursor must be a nonempty opaque value without control characters.");
            path += $"?limit={call.Limit}";
            if (call.Cursor is not null) path += "&cursor=" + Uri.EscapeDataString(call.Cursor);
        }
        using var request = new HttpRequestMessage(requestContract is null ? HttpMethod.Get : HttpMethod.Post, "api/v2/" + path);
        if (requestContract is not null)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            if (call.Request is null || call.Request.Length is 0 or > 1_048_576)
                throw new ArgumentException("A JSON request of at most 1 MiB is required.");
            V2Contracts.Validate(call.Request, requestContract);
            request.Headers.Add("Idempotency-Key", call.IdempotencyKey);
            request.Content = new ByteArrayContent(call.Request);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        if (call.Operation == V2Operation.SetupAmend)
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag returned by the setup API.");
            request.Headers.IfMatch.Add(tag);
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await HandleResponseAsync<JsonElement>(response, path, cancellationToken);
            throw new InvalidOperationException("Unreachable response.");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        JsonElement body;
        try { body = V2Contracts.Validate(bytes, responseContract); }
        catch (Exception error) when (error is ArgumentException or JsonException) { throw new InvalidOperationException("Unsupported or malformed API response: " + error.Message, error); }
        if (body.GetProperty("schemaVersion").GetInt32() != 2 || body.GetProperty("contract").GetString() != responseContract)
            throw new InvalidOperationException($"Unsupported API contract; expected {responseContract} v2.");
        return new V2Response((int)response.StatusCode, response.Headers.ETag?.ToString(), response.Headers.Location?.ToString(), body);
    }

    private static string Segment(string? value)
    {
        HeaderToken(value, "Identifier", 200);
        if (value is "." or "..") throw new ArgumentException("A path identifier cannot be a dot segment.");
        return Uri.EscapeDataString(value!);
    }

    private static void HeaderToken(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Trim() != value || value.Any(char.IsControl))
            throw new ArgumentException($"{name} must be nonempty, bounded and contain no control or surrounding whitespace.");
    }
}
