using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient
{
    private async Task<V2Response> SendWalletCapitalAsync(V2Call call, CancellationToken token)
    {
        RequireV2Credentials(requireWorkspace: true);
        var create = call.Operation == V2Operation.WalletCapitalCreate;
        var list = call.Operation == V2Operation.WalletCapitalList;
        var path = $"api/v2/wallets/{Segment(call.Id)}/capital-movements";
        if (!create && !list) path += "/" + Segment(call.SubId);
        if (list)
        {
            if (call.Limit is < 1 or > 200) throw new ArgumentException("Page limit must be between 1 and 200.");
            path += "?limit=" + call.Limit.ToString(CultureInfo.InvariantCulture);
        }
        if (!create && call.Cursor is not null)
        {
            HeaderToken(call.Cursor, "Cursor", 16_384);
            path += (list ? "&cursor=" : "?cursor=") + Uri.EscapeDataString(call.Cursor);
        }
        using var request = new HttpRequestMessage(create ? HttpMethod.Post : HttpMethod.Get, path);
        JsonElement? input = null;
        if (create)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            if (call.Request is null || call.Request.Length is 0 or > 65_536)
                throw new ArgumentException("A capital request of between 1 byte and 64 KiB is required.");
            input = V2Contracts.Validate(call.Request, "wallet-capital-create-request");
            var allowed = new HashSet<string>(["kind", "assetId", "amount", "occurredAtUtc", "reason"], StringComparer.Ordinal);
            if (input.Value.EnumerateObject().Any(p => !allowed.Contains(p.Name))
                || string.IsNullOrWhiteSpace(input.Value.GetProperty("reason").GetString()))
                throw new ArgumentException("Capital requests require explicit deposit/withdrawal evidence and a reason.");
            HeaderToken(input.Value.GetProperty("assetId").GetString(), "Asset identifier", 200);
            request.Headers.Add("Idempotency-Key", call.IdempotencyKey);
            request.Content = new ByteArrayContent(call.Request);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await _httpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Wallet capital API request failed with status {(int)response.StatusCode} ({response.StatusCode}).", null, response.StatusCode);
        JsonElement body;
        try { body = V2Contracts.Validate(await response.Content.ReadAsByteArrayAsync(token), list ? "wallet-capital-list" : "wallet-capital"); }
        catch (Exception error) when (error is ArgumentException or JsonException)
        { throw new InvalidOperationException("Unsupported or malformed wallet capital response.", error); }
        var asOf = body.GetProperty("asOf").GetDateTimeOffset();
        var watermark = body.GetProperty("publicationWatermark").GetInt64();
        if (body.GetProperty("schemaVersion").GetInt32() != 2
            || body.GetProperty("contract").GetString() != (list ? "wallet-capital-list" : "wallet-capital")
            || string.IsNullOrWhiteSpace(body.GetProperty("snapshotCursor").GetString()))
            throw new InvalidOperationException("Wallet capital response selection is invalid.");
        if (list)
        {
            var rows = body.GetProperty("items").EnumerateArray().ToArray();
            if (rows.Length > call.Limit || rows.Select(r => r.GetProperty("movementId").GetString()).Distinct(StringComparer.Ordinal).Count() != rows.Length
                || (body.GetProperty("nextCursor").ValueKind == JsonValueKind.String
                    && (string.IsNullOrWhiteSpace(body.GetProperty("nextCursor").GetString()) || body.GetProperty("nextCursor").GetString() == call.Cursor)))
                throw new InvalidOperationException("Wallet capital page is inconsistent.");
            (long Sequence, string Id)? previous = null;
            foreach (var row in rows)
            {
                ValidateCapitalMovement(row, call.Id!, asOf, watermark);
                var current = (Sequence: row.GetProperty("publicationSequence").GetInt64(), Id: row.GetProperty("movementId").GetString()!);
                if (previous is { } prior && (current.Sequence < prior.Sequence || (current.Sequence == prior.Sequence && string.CompareOrdinal(current.Id, prior.Id) <= 0)))
                    throw new InvalidOperationException("Wallet capital page ordering is inconsistent.");
                previous = current;
            }
        }
        else
        {
            var movement = body.GetProperty("data");
            ValidateCapitalMovement(movement, call.Id!, asOf, watermark);
            var id = movement.GetProperty("movementId").GetString();
            var etag = response.Headers.ETag?.ToString();
            if ((!create && id != call.SubId) || etag != $"\"wallet-capital-{id}\"")
                throw new InvalidOperationException("Wallet capital identity or ETag does not match the request.");
            if (create && (movement.GetProperty("kind").GetString() != input!.Value.GetProperty("kind").GetString()
                || movement.GetProperty("assetId").GetString() != input.Value.GetProperty("assetId").GetString()
                || ExactCapitalAmount(movement.GetProperty("amount")) != ExactCapitalAmount(input.Value.GetProperty("amount"))
                || movement.GetProperty("occurredAtUtc").GetDateTimeOffset() != input.Value.GetProperty("occurredAtUtc").GetDateTimeOffset()
                || movement.GetProperty("reason").GetString() != input.Value.GetProperty("reason").GetString()!.Trim()))
                throw new InvalidOperationException("Wallet capital response does not match the submitted evidence.");
            if (create && response.Headers.Location is { } location && location.ToString() != $"/api/v2/wallets/{Segment(call.Id)}/capital-movements/{Segment(id)}")
                throw new InvalidOperationException("Wallet capital response location is inconsistent.");
        }
        return new((int)response.StatusCode, response.Headers.ETag?.ToString(), response.Headers.Location?.ToString(), body);
    }

    private static decimal ExactCapitalAmount(JsonElement value) => decimal.Parse(value.GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static void ValidateCapitalMovement(JsonElement movement, string wallet, DateTimeOffset asOf, long watermark)
    {
        foreach (var name in new[] { "movementId", "walletId", "economicEventId", "ledgerTransactionId", "assetId", "recordedByUserId" })
            HeaderToken(movement.GetProperty(name).GetString(), "Capital identity", 200);
        var known = movement.GetProperty("knownAtUtc").GetDateTimeOffset();
        var occurred = movement.GetProperty("occurredAtUtc").GetDateTimeOffset();
        if (movement.GetProperty("walletId").GetString() != wallet
            || movement.GetProperty("source").GetString() != "userDeclaredPaperCapital"
            || movement.GetProperty("ledgerPolicyVersion").GetString() != "paper-wallet-capital-ledger-v1"
            || movement.GetProperty("publicationSequence").GetInt64() is <= 0
            || movement.GetProperty("publicationSequence").GetInt64() > watermark
            || known > asOf || occurred > known || occurred.UtcTicks % 10 != 0 || ExactCapitalAmount(movement.GetProperty("amount")) <= 0m)
            throw new InvalidOperationException("Wallet capital evidence does not match its wallet, source or knowledge coordinate.");
    }
}
