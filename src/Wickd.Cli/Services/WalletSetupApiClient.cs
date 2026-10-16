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
            V2Operation.MarketSnapshotGet => ($"market-history/snapshots/{Segment(call.Id)}", "market-history", (string?)null),
            V2Operation.MarketSeriesCoverage => ($"market-history/series/{Segment(call.Id)}/coverage", "market-coverage", null),
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
            V2Operation.SetupSkip or V2Operation.SetupMissed or V2Operation.SetupCancel =>
                ($"setups/{Segment(call.Id)}/dispositions", "journal-setup", "setup-disposition-request"),
            V2Operation.SetupNoteAppend or V2Operation.SetupNoteRevise =>
                ($"setups/{Segment(call.Id)}/notes", "journal-setup", "setup-note-request"),
            V2Operation.SetupAttemptCreate =>
                ($"setups/{Segment(call.Id)}/attempts", "setup-attempt", "setup-attempt-create-request"),
            V2Operation.SetupAttemptList =>
                ($"setups/{Segment(call.Id)}/attempts", "setup-attempt-list", null),
            V2Operation.SetupAttemptGet =>
                ($"setups/{Segment(call.Id)}/attempts/{Segment(call.SubId)}", "setup-attempt", null),
            V2Operation.PaperFillCreate => ($"wallets/{Segment(call.Id)}/fills", "paper-fill", "paper-fill-create-request"),
            V2Operation.PaperFillList => ($"wallets/{Segment(call.Id)}/fills", "paper-fill-list", null),
            V2Operation.PaperFillGet => ($"wallets/{Segment(call.Id)}/fills/{Segment(call.SubId)}", "paper-fill", null),
            V2Operation.PaperFillCorrect => ($"wallets/{Segment(call.Id)}/fills/{Segment(call.SubId)}/corrections", "paper-fill", "paper-fill-correction-request"),
            _ => throw new ArgumentException("Unknown v2 operation.")
        };
        RequireV2Credentials(requireWorkspace: !path.StartsWith("catalog/", StringComparison.Ordinal));
        if (call.Operation is V2Operation.WalletList or V2Operation.SetupList or V2Operation.SetupAttemptList or V2Operation.PaperFillList)
        {
            if (call.Limit is < 1 or > 100) throw new ArgumentException("Page limit must be between 1 and 100.");
            path += $"?limit={call.Limit}";
        }
        if (call.Cursor is not null && (call.Operation is V2Operation.WalletList or V2Operation.SetupList or V2Operation.SetupGet
            or V2Operation.SetupAttemptList or V2Operation.SetupAttemptGet or V2Operation.PaperFillList or V2Operation.PaperFillGet))
        {
            if (string.IsNullOrWhiteSpace(call.Cursor) || call.Cursor.Trim() != call.Cursor || call.Cursor.Any(char.IsControl))
                throw new ArgumentException("Cursor must be a nonempty opaque value without surrounding whitespace or control characters.");
            path += (call.Operation is V2Operation.SetupGet or V2Operation.SetupAttemptGet or V2Operation.PaperFillGet ? "?cursor=" : "&cursor=") + Uri.EscapeDataString(call.Cursor);
        }
        using var request = new HttpRequestMessage(requestContract is null ? HttpMethod.Get : HttpMethod.Post, "api/v2/" + path);
        if (requestContract is not null)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            if (call.Request is null || call.Request.Length is 0 or > 1_048_576)
                throw new ArgumentException("A JSON request of at most 1 MiB is required.");
            var requestBody = V2Contracts.Validate(call.Request, requestContract);
            RequireLifecycleIntent(call.Operation, requestBody);
            request.Headers.Add("Idempotency-Key", call.IdempotencyKey);
            request.Content = new ByteArrayContent(call.Request);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        if (call.Operation is V2Operation.SetupAmend or V2Operation.SetupSkip or V2Operation.SetupMissed
            or V2Operation.SetupCancel or V2Operation.SetupNoteAppend or V2Operation.SetupNoteRevise)
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag returned by the setup API.");
            request.Headers.IfMatch.Add(tag);
        }
        if (call.Operation == V2Operation.SetupAttemptCreate)
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag returned by the setup API.");
            request.Headers.IfMatch.Add(tag);
        }
        if (call.Operation == V2Operation.PaperFillCorrect)
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag returned by the wallet paper fills API.");
            request.Headers.IfMatch.Add(tag);
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (call.Operation is V2Operation.MarketSnapshotGet or V2Operation.MarketSeriesCoverage)
                throw MarketHistoryFailure(response);
            if (call.Operation is V2Operation.PaperFillCreate or V2Operation.PaperFillList or V2Operation.PaperFillGet or V2Operation.PaperFillCorrect)
                throw PaperFillFailure(response);
            await HandleResponseAsync<JsonElement>(response, path, cancellationToken);
            throw new InvalidOperationException("Unreachable response.");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        JsonElement body;
        try { body = V2Contracts.Validate(bytes, responseContract); }
        catch (Exception error) when (error is ArgumentException or JsonException) { throw new InvalidOperationException("Unsupported or malformed API response: " + error.Message, error); }
        if (body.GetProperty("schemaVersion").GetInt32() != 2 || body.GetProperty("contract").GetString() != responseContract)
            throw new InvalidOperationException($"Unsupported API contract; expected {responseContract} v2.");
        if (call.Operation is V2Operation.SetupAttemptCreate or V2Operation.SetupAttemptGet)
        {
            var attempt = body.GetProperty("data");
            if (!string.Equals(attempt.GetProperty("setupId").GetString(), call.Id, StringComparison.Ordinal)
                || (call.Operation == V2Operation.SetupAttemptGet
                    && !string.Equals(attempt.GetProperty("attemptId").GetString(), call.SubId, StringComparison.Ordinal)))
                throw new InvalidOperationException("Setup attempt response does not match the requested setup and attempt identities.");
        }
        else if (call.Operation == V2Operation.SetupAttemptList
            && body.GetProperty("items").EnumerateArray().Any(item =>
                !string.Equals(item.GetProperty("setupId").GetString(), call.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException("Setup attempt list contains an item from another setup.");
        else if (call.Operation is V2Operation.PaperFillCreate or V2Operation.PaperFillGet or V2Operation.PaperFillCorrect)
        {
            var fill = body.GetProperty("data");
            if (!string.Equals(fill.GetProperty("walletId").GetString(), call.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper fill response does not match the requested wallet identity.");
            if (call.Operation == V2Operation.PaperFillGet
                && !string.Equals(fill.GetProperty("fillId").GetString(), call.SubId, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper fill response does not match the requested fill identity.");
            if (call.Operation == V2Operation.PaperFillCorrect
                && !string.Equals(fill.GetProperty("replacesFillId").GetString(), call.SubId, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper fill correction response does not match the requested predecessor fill identity.");
        }
        else if (call.Operation == V2Operation.PaperFillList
            && body.GetProperty("items").EnumerateArray().Any(item =>
                !string.Equals(item.GetProperty("walletId").GetString(), call.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException("Paper fill list contains an item from another wallet.");
        return new V2Response((int)response.StatusCode, response.Headers.ETag?.ToString(), response.Headers.Location?.ToString(), body);
    }

    // Retained-data errors may originate in storage. Never echo arbitrary upstream
    // bodies, storage paths, credentials, or reason phrases into public CLI output.
    private static HttpRequestException MarketHistoryFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var explanation = status switch
        {
            404 => "The snapshot or series is unavailable in this workspace; current membership and source entitlement are required.",
            503 => "Retained evidence is unavailable; the server must configure and verify its retained storage.",
            401 => "A current user session is required.",
            403 => "The current user is not allowed to read this evidence.",
            _ => "The server refused the retained-history read."
        };
        return new HttpRequestException($"Market history request failed with HTTP {status}. {explanation}", null, response.StatusCode);
    }

    private static HttpRequestException PaperFillFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var explanation = status switch
        {
            401 => "A current user session is required.",
            403 => "The current user cannot access this wallet in the selected workspace.",
            404 => "The wallet or fill is unavailable in the selected workspace.",
            409 => "The write conflicts with current wallet fill state; inspect the latest response and preserve this request for a safe retry.",
            412 => "The wallet fill ETag is stale; read current fills before correcting.",
            428 => "A strong If-Match value is required for correction.",
            503 => "The paper fill service is unavailable; the server must configure its required persistence.",
            _ => "The server refused the paper fill request."
        };
        return new HttpRequestException($"Paper fill request failed with HTTP {status}. {explanation}", null, response.StatusCode);
    }

    private static void RequireLifecycleIntent(V2Operation operation, JsonElement body)
    {
        var disposition = operation switch
        {
            V2Operation.SetupSkip => "markSkipped",
            V2Operation.SetupMissed => "markMissed",
            V2Operation.SetupCancel => "cancel",
            _ => null
        };
        if (disposition is not null && body.GetProperty("command").GetString() != disposition)
            throw new ArgumentException($"This command requires request.command '{disposition}'. The original request file is sent unchanged.");
        if (operation is V2Operation.SetupNoteAppend or V2Operation.SetupNoteRevise)
        {
            var supersedes = body.GetProperty("supersedesId");
            if (operation == V2Operation.SetupNoteAppend && supersedes.ValueKind != JsonValueKind.Null)
                throw new ArgumentException("Append requires supersedesId: null. Use 'setup notes revise' to revise an existing note.");
            if (operation == V2Operation.SetupNoteRevise && (supersedes.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(supersedes.GetString())))
                throw new ArgumentException("Revise requires the supersedesId of the note being revised.");
        }
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
