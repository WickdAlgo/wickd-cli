using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient
{
    private static readonly string[] DeclarationClaims = ["openingNonCashHoldingsKnownZero", "openingPositionsKnownFlat",
        "liabilitiesKnownZeroThroughout", "allManualFillsRecorded", "allCapitalMovementsRecorded"];

    private async Task<V2Response> SendPaperWalletDeclarationAsync(V2Call call, CancellationToken token)
    {
        RequireV2Credentials(requireWorkspace: true);
        var create = call.Operation == V2Operation.WalletDeclarationCreate;
        var correct = call.Operation == V2Operation.WalletDeclarationCorrect;
        var list = call.Operation == V2Operation.WalletDeclarationList;
        var write = create || correct;
        var collection = $"api/v2/wallets/{Segment(call.Id)}/completeness-declarations";
        var path = create || list ? collection : collection + "/" + Segment(call.SubId) + (correct ? "/corrections" : "");
        if (list)
        {
            if (call.Limit is < 1 or > 10) throw new ArgumentException("Page limit must be between 1 and 10.");
            path += "?limit=" + call.Limit.ToString(CultureInfo.InvariantCulture);
        }
        if (!write && call.Cursor is not null)
        {
            HeaderToken(call.Cursor, "Cursor", 16_384);
            path += (list ? "&cursor=" : "?cursor=") + Uri.EscapeDataString(call.Cursor);
        }
        using var request = new HttpRequestMessage(write ? HttpMethod.Post : HttpMethod.Get, path);
        JsonElement? input = null;
        if (write)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            HeaderToken(call.IfMatch, "If-Match", 240);
            if (call.IfMatch!.StartsWith("W/", StringComparison.Ordinal) || !call.IfMatch.StartsWith('"') || !call.IfMatch.EndsWith('"'))
                throw new ArgumentException("A strong declaration stream ETag from the list response is required.");
            if (call.Request is null || call.Request.Length is 0 or > 65_536)
                throw new ArgumentException("A declaration request of between 1 byte and 64 KiB is required.");
            input = V2Contracts.Validate(call.Request, correct ? "paper-wallet-declaration-correction-request" : "paper-wallet-declaration-create-request");
            var replacement = correct ? input.Value.GetProperty("replacement") : input.Value;
            if ((correct && (input.Value.EnumerateObject().Count() != 2 || string.IsNullOrWhiteSpace(input.Value.GetProperty("correctionReason").GetString())))
                || replacement.EnumerateObject().Count() != 3 || string.IsNullOrWhiteSpace(replacement.GetProperty("reason").GetString())
                || replacement.GetProperty("claims").EnumerateObject().Count() != DeclarationClaims.Length)
                throw new ArgumentException("Declarations require every nullable claim, an explanation and nothing else.");
            request.Headers.Add("Idempotency-Key", call.IdempotencyKey);
            request.Headers.TryAddWithoutValidation("If-Match", call.IfMatch);
            request.Content = new ByteArrayContent(call.Request);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await _httpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Paper wallet declaration API request failed with status {(int)response.StatusCode} ({response.StatusCode}).", null, response.StatusCode);
        var contract = list ? "paper-wallet-declaration-list" : "paper-wallet-declaration";
        JsonElement body;
        try { body = V2Contracts.Validate(await response.Content.ReadAsByteArrayAsync(token), contract); }
        catch (Exception error) when (error is ArgumentException or JsonException)
        { throw new InvalidOperationException("Unsupported or malformed paper wallet declaration response.", error); }
        var asOf = body.GetProperty("asOf").GetDateTimeOffset();
        var watermark = body.GetProperty("publicationWatermark").GetInt64();
        var etag = response.Headers.ETag?.ToString();
        if (body.GetProperty("schemaVersion").GetInt32() != 2 || body.GetProperty("contract").GetString() != contract
            || string.IsNullOrWhiteSpace(body.GetProperty("snapshotCursor").GetString())
            || body.GetProperty("streamRevision").GetInt64() is var revision && (revision < 0 || revision > watermark)
            || etag is null || response.Headers.ETag!.IsWeak || !etag.StartsWith("\"paper-wallet-declaration-", StringComparison.Ordinal))
            throw new InvalidOperationException("Paper wallet declaration response selection or stream ETag is invalid.");
        if (list)
        {
            var rows = body.GetProperty("items").EnumerateArray().ToArray();
            var ids = rows.Select(r => r.GetProperty("declaration").GetProperty("declarationId").GetString()).ToArray();
            if (rows.Length > call.Limit || ids.Distinct(StringComparer.Ordinal).Count() != rows.Length
                || (body.GetProperty("nextCursor").ValueKind == JsonValueKind.String
                    && (string.IsNullOrWhiteSpace(body.GetProperty("nextCursor").GetString()) || body.GetProperty("nextCursor").GetString() == call.Cursor)))
                throw new InvalidOperationException("Paper wallet declaration page is inconsistent.");
            (long Sequence, string Id)? previous = null;
            foreach (var row in rows)
            {
                ValidateDeclarationRead(row, call.Id!, asOf, watermark);
                var declaration = row.GetProperty("declaration");
                var current = (Sequence: declaration.GetProperty("publicationSequence").GetInt64(), Id: declaration.GetProperty("declarationId").GetString()!);
                if (previous is { } prior && (current.Sequence < prior.Sequence || (current.Sequence == prior.Sequence && string.CompareOrdinal(current.Id, prior.Id) <= 0)))
                    throw new InvalidOperationException("Paper wallet declaration page ordering is inconsistent.");
                previous = current;
            }
        }
        else
        {
            var read = body.GetProperty("data");
            ValidateDeclarationRead(read, call.Id!, asOf, watermark);
            var declaration = read.GetProperty("declaration");
            var id = declaration.GetProperty("declarationId").GetString()!;
            if (!write && id != call.SubId) throw new InvalidOperationException("Paper wallet declaration identity does not match the request.");
            if (write)
            {
                var replacement = correct ? input!.Value.GetProperty("replacement") : input!.Value;
                if (declaration.GetProperty("throughOccurredAtUtc").GetDateTimeOffset() != replacement.GetProperty("throughOccurredAtUtc").GetDateTimeOffset()
                    || declaration.GetProperty("reason").GetString() != replacement.GetProperty("reason").GetString()!.Trim()
                    || DeclarationClaims.Any(name => !SameClaim(declaration.GetProperty("claims").GetProperty(name), replacement.GetProperty("claims").GetProperty(name)))
                    || (correct ? declaration.GetProperty("replacesDeclarationId").GetString() != call.SubId
                        || declaration.GetProperty("correctionReason").GetString() != input.Value.GetProperty("correctionReason").GetString()!.Trim()
                        : declaration.GetProperty("revision").GetInt32() != 1)
                    || (response.Headers.Location is { } location && location.ToString() != $"/api/v2/wallets/{Segment(call.Id)}/completeness-declarations/{Segment(id)}"))
                    throw new InvalidOperationException("Paper wallet declaration response does not match the submitted assertions.");
            }
        }
        return new((int)response.StatusCode, etag, response.Headers.Location?.ToString(), body);
    }

    private static bool SameClaim(JsonElement served, JsonElement submitted) => served.ValueKind == submitted.ValueKind;

    private static void ValidateDeclarationRead(JsonElement read, string wallet, DateTimeOffset asOf, long watermark)
    {
        var declaration = read.GetProperty("declaration");
        foreach (var name in new[] { "declarationId", "walletId", "recordedByUserId", "rootDeclarationId" })
            HeaderToken(declaration.GetProperty(name).GetString(), "Declaration identity", 200);
        var sequence = declaration.GetProperty("publicationSequence").GetInt64();
        var known = declaration.GetProperty("knownAtUtc").GetDateTimeOffset();
        var opening = declaration.GetProperty("openingAtUtc").GetDateTimeOffset();
        var through = declaration.GetProperty("throughOccurredAtUtc").GetDateTimeOffset();
        var reconciliation = read.GetProperty("reconciliation");
        var limitations = reconciliation.GetProperty("limitations").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        // The CLI never presents a raw assertion as verified inventory or a valuation: the server must say so explicitly.
        if (declaration.GetProperty("walletId").GetString() != wallet
            || declaration.GetProperty("source").GetString() != "userDeclaredPaperWalletZeroOnly"
            || sequence <= 0 || sequence > watermark || declaration.GetProperty("inputWatermark").GetInt64() != sequence - 1
            || declaration.GetProperty("inputKnownAtUtc").GetDateTimeOffset() != known
            || known > asOf || opening > through || through > known
            || reconciliation.GetProperty("evaluationWatermark").GetInt64() is var evaluated && (evaluated < sequence || evaluated > watermark)
            || !limitations.IsSupersetOf(["userDeclarationOnly", "noIndependentCompletenessProof", "noNumericalValuation"]))
            throw new InvalidOperationException("Paper wallet declaration does not match its wallet, source, limitations or knowledge coordinate.");
    }
}
