using System.Net.Http.Headers;
using System.Text.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient
{
    private static bool IsSetupReviewOperation(V2Operation operation) => operation is
        V2Operation.SetupReviewAppend or V2Operation.SetupReviewGet or
        V2Operation.SetupReviewRevisionGet or V2Operation.SetupReviewHistory;

    private async Task<V2Response> SendSetupReviewAsync(V2Call call, CancellationToken cancellationToken)
    {
        var (route, contract, requestContract, method) = call.Operation switch
        {
            V2Operation.SetupReviewAppend => ($"setups/{Segment(call.Id)}/reviews", "setup-review-revision", "setup-review-request", HttpMethod.Post),
            V2Operation.SetupReviewGet => ($"setups/{Segment(call.Id)}/review", "setup-review-view", (string?)null, HttpMethod.Get),
            V2Operation.SetupReviewRevisionGet => ($"setups/{Segment(call.Id)}/reviews/{Segment(call.SubId)}", "setup-review-revision", (string?)null, HttpMethod.Get),
            V2Operation.SetupReviewHistory => ($"setups/{Segment(call.Id)}/reviews", "setup-review-page", (string?)null, HttpMethod.Get),
            _ => throw new ArgumentException("Unknown setup-review operation.")
        };

        RequireV2Credentials(requireWorkspace: true);
        if (call.Operation == V2Operation.SetupReviewHistory)
        {
            if (call.Limit is < 1 or > 200) throw new ArgumentException("Review page limit must be between 1 and 200.");
            route += $"?limit={call.Limit}";
        }
        if (call.Cursor is not null)
        {
            ValidateCursor(call.Cursor);
            route += (route.Contains('?') ? "&" : "?") + "cursor=" + Uri.EscapeDataString(call.Cursor);
        }

        using var request = new HttpRequestMessage(method, "api/v2/" + route);
        if (requestContract is not null)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            if (call.Request is null || call.Request.Length is 0 or > 65_536)
                throw new ArgumentException("A JSON request of at most 64 KiB is required for setup-review writes.");
            _ = V2Contracts.Validate(call.Request, requestContract);
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the exact strong ETag from the setup review read.");
            request.Headers.Add("Idempotency-Key", call.IdempotencyKey);
            request.Headers.IfMatch.Add(tag);
            request.Content = new ByteArrayContent(call.Request);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Setup-review API request failed with status {(int)response.StatusCode} ({response.StatusCode}).", null, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        JsonElement body;
        try { body = V2Contracts.Validate(bytes, contract); }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            throw new InvalidOperationException("Unsupported or malformed setup-review API response: " + error.Message, error);
        }
        if (body.GetProperty("schemaVersion").GetInt32() != 2 || body.GetProperty("contract").GetString() != contract)
            throw new InvalidOperationException($"Unsupported API contract; expected {contract} v2.");
        ValidateSetupReviewIdentity(call, body.GetProperty("data"));
        var etag = response.Headers.ETag?.ToString();
        if (call.Operation is V2Operation.SetupReviewAppend or V2Operation.SetupReviewGet or V2Operation.SetupReviewRevisionGet
            && (!EntityTagHeaderValue.TryParse(etag, out var responseTag) || responseTag.IsWeak || responseTag.Tag == "*"))
            throw new InvalidOperationException("Setup-review response omitted its strong review-stream ETag.");
        return new V2Response((int)response.StatusCode, etag, response.Headers.Location?.ToString(), body);
    }

    private static void ValidateSetupReviewIdentity(V2Call call, JsonElement data)
    {
        static string Required(JsonElement value, string property) => value.GetProperty(property).GetString()
            ?? throw new InvalidOperationException($"Setup-review response omitted {property}.");
        void Matches(JsonElement value, string property, string expected)
        {
            if (!string.Equals(Required(value, property), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Setup-review response identity does not match the requested resource.");
        }

        switch (call.Operation)
        {
            case V2Operation.SetupReviewAppend:
                Matches(data, "setupId", call.Id!);
                break;
            case V2Operation.SetupReviewGet:
                Matches(data, "setupId", call.Id!);
                if (data.GetProperty("review") is { ValueKind: JsonValueKind.Object } review)
                    Matches(review, "setupId", call.Id!);
                break;
            case V2Operation.SetupReviewRevisionGet:
                Matches(data, "setupId", call.Id!);
                Matches(data, "reviewId", call.SubId!);
                break;
            case V2Operation.SetupReviewHistory:
                if (data.GetProperty("items").EnumerateArray().Any(item =>
                    !string.Equals(item.GetProperty("setupId").GetString(), call.Id, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Setup-review history contains a different setup identity.");
                break;
        }
    }
}
