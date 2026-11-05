using System.Net.Http.Headers;
using System.Text.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient
{
    public async Task<V2Response> SendV2Async(V2Call call, CancellationToken cancellationToken = default)
    {
        if (call.Operation is V2Operation.PerformanceReportCreate or V2Operation.PerformanceReportGet or V2Operation.PerformanceReportRevise)
            return await SendPerformanceReportAsync(call, cancellationToken);
        if (IsStrategyExampleOperation(call.Operation))
            return await SendStrategyExampleAsync(call, cancellationToken);
        if (IsSetupReviewOperation(call.Operation))
            return await SendSetupReviewAsync(call, cancellationToken);
        if (IsStrategyContextOperation(call.Operation))
            return await SendStrategyContextAsync(call, cancellationToken);

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
            V2Operation.PaperHistoryCreate => (WalletInstrumentPath(call, "history-attestations"), "paper-history", "paper-history-create-request"),
            V2Operation.PaperHistoryList => (WalletInstrumentPath(call, "history-attestations"), "paper-history-list", null),
            V2Operation.PaperHistoryGet => (WalletInstrumentPath(call, $"history-attestations/{Segment(call.SubId)}"), "paper-history", null),
            V2Operation.PaperHistoryCorrect => (WalletInstrumentPath(call, $"history-attestations/{Segment(call.SubId)}/corrections"), "paper-history", "paper-history-correction-request"),
            V2Operation.WalletAccountingCreate => (WalletInstrumentPath(call, "accounting-receipts"), "wallet-accounting", "wallet-accounting-create-request"),
            V2Operation.WalletAccountingList => (WalletInstrumentPath(call, "accounting-receipts"), "wallet-accounting-list", null),
            V2Operation.WalletAccountingGet => (WalletInstrumentPath(call, $"accounting-receipts/{Segment(call.SubId)}"), "wallet-accounting", null),
            V2Operation.PaperAllocationAssign => (WalletInstrumentPath(call, "activity-allocations"), "paper-allocation", "paper-allocation-request"),
            V2Operation.PaperAllocationList => (WalletInstrumentPath(call, "activity-allocations"), "paper-allocation-list", null),
            V2Operation.PaperAllocationGet => (WalletInstrumentPath(call, $"activity-allocations/{Segment(call.SubId)}"), "paper-allocation", null),
            V2Operation.PaperCostCreate => ($"wallets/{Segment(call.Id)}/costs", "paper-cost", "paper-cost-create-request"),
            V2Operation.PaperCostList => ($"wallets/{Segment(call.Id)}/costs", "paper-cost-list", null),
            V2Operation.PaperCostGet => ($"wallets/{Segment(call.Id)}/costs/{Segment(call.SubId)}", "paper-cost", null),
            V2Operation.PaperCostCorrect => ($"wallets/{Segment(call.Id)}/costs/{Segment(call.SubId)}/corrections", "paper-cost", "paper-cost-correction-request"),
            V2Operation.CostAttributionAssign => (CostAttributionPath(call), "cost-attribution", "cost-attribution-request"),
            V2Operation.CostAttributionList => (CostAttributionPath(call), "cost-attribution-list", null),
            V2Operation.CostAttributionGet => (CostAttributionPath(call) + $"/{Segment(call.SubId)}", "cost-attribution", null),
            _ => throw new ArgumentException("Unknown v2 operation.")
        };
        RequireV2Credentials(requireWorkspace: !path.StartsWith("catalog/", StringComparison.Ordinal));
        if (call.Operation is V2Operation.WalletList or V2Operation.SetupList or V2Operation.SetupAttemptList or V2Operation.PaperFillList
            or V2Operation.PaperHistoryList or V2Operation.WalletAccountingList or V2Operation.PaperAllocationList or V2Operation.PaperCostList or V2Operation.CostAttributionList)
        {
            if (call.Limit is < 1 or > 100) throw new ArgumentException("Page limit must be between 1 and 100.");
            path += $"?limit={call.Limit}";
        }
        if (call.Cursor is not null && (call.Operation is V2Operation.WalletList or V2Operation.SetupList or V2Operation.SetupGet
            or V2Operation.SetupAttemptList or V2Operation.SetupAttemptGet or V2Operation.PaperFillList or V2Operation.PaperFillGet
            or V2Operation.PaperHistoryList or V2Operation.PaperHistoryGet or V2Operation.WalletAccountingList or V2Operation.WalletAccountingGet
            or V2Operation.PaperAllocationList or V2Operation.PaperAllocationGet or V2Operation.PaperCostList or V2Operation.PaperCostGet or V2Operation.CostAttributionList or V2Operation.CostAttributionGet))
        {
            if (string.IsNullOrWhiteSpace(call.Cursor) || call.Cursor.Trim() != call.Cursor || call.Cursor.Any(char.IsControl))
                throw new ArgumentException("Cursor must be a nonempty opaque value without surrounding whitespace or control characters.");
            path += (call.Operation is V2Operation.SetupGet or V2Operation.SetupAttemptGet or V2Operation.PaperFillGet
                or V2Operation.PaperHistoryGet or V2Operation.WalletAccountingGet or V2Operation.PaperAllocationGet or V2Operation.PaperCostGet or V2Operation.CostAttributionGet ? "?cursor=" : "&cursor=") + Uri.EscapeDataString(call.Cursor);
        }
        if (call.Operation is V2Operation.SetupList or V2Operation.SetupGet)
            path = AppendSetupContextFilters(path, call.SessionDefinitionVersionId, call.MarketConditionTagId);
        using var request = new HttpRequestMessage(requestContract is null ? HttpMethod.Get : HttpMethod.Post, "api/v2/" + path);
        if (requestContract is not null)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            var maximumRequestBytes = call.Operation is V2Operation.PaperAllocationAssign or V2Operation.CostAttributionAssign ? 2_097_152 : 1_048_576;
            if (call.Request is null || call.Request.Length is 0 || call.Request.Length > maximumRequestBytes)
                throw new ArgumentException(call.Operation is V2Operation.PaperAllocationAssign or V2Operation.CostAttributionAssign
                    ? "A JSON request of at most 2 MiB is required." : "A JSON request of at most 1 MiB is required.");
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
        if (call.Operation is V2Operation.PaperHistoryCreate or V2Operation.PaperHistoryCorrect)
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag returned by the wallet/instrument paper history API.");
            request.Headers.IfMatch.Add(tag);
        }
        if (call.Operation == V2Operation.PaperAllocationAssign)
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag returned by the wallet/instrument allocations API.");
            request.Headers.IfMatch.Add(tag);
        }
        if (call.Operation == V2Operation.CostAttributionAssign)
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag returned by this root cost's attribution list or read.");
            request.Headers.IfMatch.Add(tag);
        }
        if (call.Operation == V2Operation.PaperCostCorrect)
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag returned by this wallet's paper costs list or read.");
            request.Headers.IfMatch.Add(tag);
        }
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (call.Operation is V2Operation.MarketSnapshotGet or V2Operation.MarketSeriesCoverage)
                throw MarketHistoryFailure(response);
            if (call.Operation is V2Operation.PaperFillCreate or V2Operation.PaperFillList or V2Operation.PaperFillGet or V2Operation.PaperFillCorrect)
                throw PaperFillFailure(response);
            if (call.Operation is V2Operation.PaperHistoryCreate or V2Operation.PaperHistoryList or V2Operation.PaperHistoryGet or V2Operation.PaperHistoryCorrect)
                throw PaperHistoryFailure(response, call.Operation);
            if (call.Operation is V2Operation.WalletAccountingCreate or V2Operation.WalletAccountingList or V2Operation.WalletAccountingGet)
                throw WalletAccountingFailure(response);
            if (call.Operation is V2Operation.PaperAllocationAssign or V2Operation.PaperAllocationList or V2Operation.PaperAllocationGet)
                throw PaperAllocationFailure(response, call.Operation);
            if (call.Operation is V2Operation.PaperCostCreate or V2Operation.PaperCostList or V2Operation.PaperCostGet or V2Operation.PaperCostCorrect)
                throw PaperCostFailure(response, call.Operation);
            if (call.Operation is V2Operation.CostAttributionAssign or V2Operation.CostAttributionList or V2Operation.CostAttributionGet)
                throw CostAttributionFailure(response);
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
        else if (call.Operation is V2Operation.PaperHistoryCreate or V2Operation.PaperHistoryGet or V2Operation.PaperHistoryCorrect)
        {
            var history = body.GetProperty("data");
            RequireWalletInstrumentIdentity(history, call, "Paper history");
            if (call.Operation == V2Operation.PaperHistoryGet
                && !string.Equals(history.GetProperty("attestationId").GetString(), call.SubId, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper history response does not match the requested attestation identity.");
            if (call.Operation == V2Operation.PaperHistoryCorrect
                && !string.Equals(history.GetProperty("replacesAttestationId").GetString(), call.SubId, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper history correction response does not match the requested predecessor attestation identity.");
        }
        else if (call.Operation == V2Operation.PaperHistoryList
            && body.GetProperty("items").EnumerateArray().Any(item => !MatchesWalletInstrumentIdentity(item, call)))
            throw new InvalidOperationException("Paper history list contains an item outside the requested wallet/instrument scope.");
        else if (call.Operation is V2Operation.WalletAccountingCreate or V2Operation.WalletAccountingGet)
        {
            var receipt = body.GetProperty("data");
            RequireWalletInstrumentIdentity(receipt, call, "Wallet accounting");
            if (call.Operation == V2Operation.WalletAccountingGet
                && !string.Equals(receipt.GetProperty("receiptId").GetString(), call.SubId, StringComparison.Ordinal))
                throw new InvalidOperationException("Wallet accounting response does not match the requested receipt identity.");
        }
        else if (call.Operation == V2Operation.WalletAccountingList
            && body.GetProperty("items").EnumerateArray().Any(item => !MatchesWalletInstrumentIdentity(item, call)))
            throw new InvalidOperationException("Wallet accounting list contains an item outside the requested wallet/instrument scope.");
        else if (call.Operation is V2Operation.PaperAllocationAssign or V2Operation.PaperAllocationGet)
        {
            var revision = body.GetProperty("data");
            RequireWalletInstrumentIdentity(revision, call, "Paper allocation");
            if (call.Operation == V2Operation.PaperAllocationGet
                && !string.Equals(revision.GetProperty("revisionId").GetString(), call.SubId, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper allocation response does not match the requested revision identity.");
            if (call.Operation == V2Operation.PaperAllocationAssign)
            {
                using var requestDocument = JsonDocument.Parse(call.Request!);
                var requestedPredecessor = requestDocument.RootElement.GetProperty("priorRevisionId");
                var actualPredecessor = revision.GetProperty("priorRevisionId");
                if (requestedPredecessor.ValueKind != actualPredecessor.ValueKind
                    || (requestedPredecessor.ValueKind == JsonValueKind.String
                        && !string.Equals(requestedPredecessor.GetString(), actualPredecessor.GetString(), StringComparison.Ordinal)))
                    throw new InvalidOperationException("Paper allocation response does not match the requested predecessor revision identity.");
            }
        }
        else if (call.Operation == V2Operation.PaperAllocationList
            && body.GetProperty("items").EnumerateArray().Any(item => !MatchesWalletInstrumentIdentity(item, call)))
            throw new InvalidOperationException("Paper allocation list contains an item outside the requested wallet/instrument scope.");
        else if (call.Operation is V2Operation.PaperCostCreate or V2Operation.PaperCostGet or V2Operation.PaperCostCorrect)
        {
            var cost = body.GetProperty("data");
            if (!string.Equals(cost.GetProperty("walletId").GetString(), call.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper cost response does not match the requested wallet identity.");
            if (call.Operation == V2Operation.PaperCostGet
                && !string.Equals(cost.GetProperty("costId").GetString(), call.SubId, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper cost response does not match the requested cost identity.");
            if (call.Operation == V2Operation.PaperCostCorrect
                && !string.Equals(cost.GetProperty("replacesCostId").GetString(), call.SubId, StringComparison.Ordinal))
                throw new InvalidOperationException("Paper cost correction response does not match the requested predecessor cost identity.");
        }
        else if (call.Operation == V2Operation.PaperCostList
            && body.GetProperty("items").EnumerateArray().Any(item =>
                !string.Equals(item.GetProperty("walletId").GetString(), call.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException("Paper cost list contains an item from another wallet.");
        else if (call.Operation is V2Operation.CostAttributionAssign or V2Operation.CostAttributionGet)
        {
            var revision = body.GetProperty("data");
            if (!MatchesCostAttributionIdentity(revision, call))
                throw new InvalidOperationException("Cost attribution response does not match the requested wallet/root cost identity.");
            if (call.Operation == V2Operation.CostAttributionGet
                && revision.GetProperty("revisionId").GetString() != call.SubId)
                throw new InvalidOperationException("Cost attribution response does not match the requested revision identity.");
            if (call.Operation == V2Operation.CostAttributionAssign)
            {
                using var requestDocument = JsonDocument.Parse(call.Request!);
                foreach (var property in new[] { "costId", "priorRevisionId" })
                {
                    var expected = requestDocument.RootElement.GetProperty(property);
                    var actual = revision.GetProperty(property);
                    if (expected.ValueKind != actual.ValueKind || expected.GetString() != actual.GetString())
                        throw new InvalidOperationException("Cost attribution response does not match the requested cost/predecessor revision identity.");
                }
            }
        }
        else if (call.Operation == V2Operation.CostAttributionList
            && body.GetProperty("items").EnumerateArray().Any(item => !MatchesCostAttributionIdentity(item, call)))
            throw new InvalidOperationException("Cost attribution list contains an item outside the requested wallet/root cost scope.");
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

    private static HttpRequestException PaperHistoryFailure(HttpResponseMessage response, V2Operation operation)
    {
        var status = (int)response.StatusCode;
        var explanation = status switch
        {
            401 => "A current user session is required.",
            403 => "The current user cannot access this wallet/instrument history in the selected workspace.",
            404 => "The wallet, instrument or attestation is unavailable in the selected workspace.",
            409 when operation == V2Operation.PaperHistoryCorrect
                => "History changed or this declaration already has a correction; read current history before starting another correction.",
            409 => "The history stream changed or already has a declaration. Read current history; if a declaration exists, use wallet history correct with its current ETag and an explicit reason.",
            412 => "The paper history ETag is stale; read current history before starting a new write.",
            428 => "A strong If-Match value is required for a history write.",
            503 => "The paper history service is unavailable; the server must configure its required persistence.",
            _ => "The server refused the paper history request."
        };
        return new HttpRequestException($"Paper history request failed with HTTP {status}. {explanation}", null, response.StatusCode);
    }

    private static HttpRequestException WalletAccountingFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var explanation = status switch
        {
            401 => "A current user session is required.",
            403 => "The current user cannot access this wallet/instrument in the selected workspace.",
            404 => "The wallet, instrument or accounting receipt is unavailable in the selected workspace.",
            409 => "The accounting command conflicts with the prior use of this idempotency key; retry with the original request bytes and key, or use a new key for a different command.",
            422 => "The server cannot produce an accounting receipt from the currently retained evidence.",
            503 => "The wallet accounting service is unavailable; the server must configure its required persistence.",
            _ => "The server refused the wallet accounting request."
        };
        return new HttpRequestException($"Wallet accounting request failed with HTTP {status}. {explanation}", null, response.StatusCode);
    }

    private static HttpRequestException PaperAllocationFailure(HttpResponseMessage response, V2Operation operation)
    {
        var status = (int)response.StatusCode;
        var explanation = status switch
        {
            401 => "A current user session is required.",
            403 => "The current user cannot access this wallet/instrument in the selected workspace.",
            404 => "The wallet, instrument or allocation revision is unavailable in the selected workspace.",
            409 => "The assignment conflicts with retained activity, receipt currentness or prior use of this idempotency key; retry unchanged bytes with the same key, or read current allocations before making a new assignment.",
            412 => "The wallet/instrument allocation ETag is stale; read current allocations before assigning again.",
            422 => "The server cannot validate this allocation against the retained paper activity receipt.",
            428 when operation == V2Operation.PaperAllocationAssign => "A strong If-Match value is required for an allocation write.",
            503 => "The paper allocation service is unavailable; the server must configure its required persistence.",
            _ => "The server refused the paper allocation request."
        };
        return new HttpRequestException($"Paper allocation request failed with HTTP {status}. {explanation}", null, response.StatusCode);
    }

    private static HttpRequestException PaperCostFailure(HttpResponseMessage response, V2Operation operation)
    {
        var status = (int)response.StatusCode;
        var explanation = status switch
        {
            401 => "A current user session is required.",
            403 => "The current user cannot access this wallet in the selected workspace.",
            404 => "The wallet or cost is unavailable in the selected workspace or frozen snapshot.",
            409 when operation == V2Operation.PaperCostCreate => "This idempotency key conflicts with an earlier cost request; retry with the original request bytes and key, or use a new key for a different cost.",
            409 => "The wallet cost stream changed or this cost already has a replacement; read current costs before starting another correction.",
            412 => "The wallet cost ETag is stale; read current costs before correcting.",
            422 => "The server cannot record this manual cost for the selected wallet.",
            428 when operation == V2Operation.PaperCostCorrect => "A strong If-Match value is required for a cost correction.",
            503 => "The paper cost service is unavailable; the server must configure its required persistence.",
            _ => "The server refused the paper cost request."
        };
        return new HttpRequestException($"Paper cost request failed with HTTP {status}. {explanation}", null, response.StatusCode);
    }

    private static string CostAttributionPath(V2Call call) =>
        $"wallets/{Segment(call.Id)}/costs/{Segment(call.RootCostId)}/attributions";

    private static bool MatchesCostAttributionIdentity(JsonElement item, V2Call call) =>
        item.GetProperty("walletId").GetString() == call.Id
        && item.GetProperty("rootCostId").GetString() == call.RootCostId;

    private static HttpRequestException CostAttributionFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var explanation = status switch
        {
            401 => "A current user session is required.",
            403 => "The current user cannot access this wallet in the selected workspace.",
            404 => "The wallet, root cost or attribution revision is unavailable in the selected workspace or frozen snapshot.",
            409 => "The assignment conflicts with the current cost, prior revision or idempotency key; retry unchanged bytes with the same key, or read current cost attribution before a new assignment.",
            412 => "The root cost attribution ETag is stale; read current attribution before assigning again.",
            422 => "The server cannot validate this attribution against the retained paper cost.",
            428 => "A strong If-Match value is required for an attribution write.",
            503 => "The paper cost attribution service is unavailable; the server must configure its required persistence.",
            _ => "The server refused the paper cost attribution request."
        };
        return new HttpRequestException($"Cost attribution request failed with HTTP {status}. {explanation}", null, response.StatusCode);
    }

    private static string WalletInstrumentPath(V2Call call, string suffix) =>
        $"wallets/{Segment(call.Id)}/instruments/{Segment(call.InstrumentId)}/{suffix}";

    private static bool MatchesWalletInstrumentIdentity(JsonElement item, V2Call call) =>
        string.Equals(item.GetProperty("walletId").GetString(), call.Id, StringComparison.Ordinal)
        && string.Equals(item.GetProperty("instrumentId").GetString(), call.InstrumentId, StringComparison.Ordinal);

    private static void RequireWalletInstrumentIdentity(JsonElement item, V2Call call, string label)
    {
        if (!MatchesWalletInstrumentIdentity(item, call))
            throw new InvalidOperationException($"{label} response does not match the requested wallet/instrument identity.");
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
