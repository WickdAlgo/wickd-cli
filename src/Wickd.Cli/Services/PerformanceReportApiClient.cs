using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient
{
    private async Task<V2Response> SendPerformanceReportAsync(V2Call call, CancellationToken token)
    {
        RequireV2Credentials(requireWorkspace: true);
        var create = call.Operation == V2Operation.PerformanceReportCreate;
        var revise = call.Operation == V2Operation.PerformanceReportRevise;
        var route = create ? "api/v2/performance/queries" : "api/v2/performance/results/" + Segment(call.Id);
        if (revise) route += "/revisions";
        using var request = new HttpRequestMessage(create || revise ? HttpMethod.Post : HttpMethod.Get, route);
        JsonElement? input = null;
        if (create || revise)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            if (call.Request is null || call.Request.Length is 0 or > 65_536)
                throw new ArgumentException("A performance request of between 1 byte and 64 KiB is required.");
            input = V2Contracts.Validate(call.Request, create ? "setup-performance-query-request" : "setup-performance-report-revision-request");
            request.Headers.Add("Idempotency-Key", call.IdempotencyKey);
            request.Content = new ByteArrayContent(call.Request);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (revise)
            {
                HeaderToken(call.IfMatch, "If-Match", 200);
                if (!IsStrongPerformanceETag(call.IfMatch)) throw new ArgumentException("Revision requires one strong If-Match ETag.");
                if (string.IsNullOrWhiteSpace(input.Value.GetProperty("reason").GetString()))
                    throw new ArgumentException("Revision reason must contain non-whitespace text.");
                request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(call.IfMatch!));
            }
        }
        using var response = await _httpClient.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            throw await PerformanceConflictAsync(response, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Performance API request failed with status {(int)response.StatusCode} ({response.StatusCode}).", null, response.StatusCode);
        JsonElement body;
        try { body = V2Contracts.Validate(await response.Content.ReadAsByteArrayAsync(token), "setup-performance-report"); }
        catch (Exception error) when (error is ArgumentException or JsonException)
        { throw new InvalidOperationException("Unsupported or malformed performance response.", error); }
        var data = body.GetProperty("data");
        var report = data.GetProperty("report");
        var id = data.GetProperty("resultId").GetString();
        var etag = response.Headers.ETag?.ToString();
        if (body.GetProperty("schemaVersion").GetInt32() != 2 || body.GetProperty("contract").GetString() != "setup-performance-report"
            || !IsStrongPerformanceETag(etag) || etag != data.GetProperty("eTag").GetString()
            || report.GetProperty("reportId").GetString() != id
            || (!create && !revise && id != call.Id)
            || (revise && data.GetProperty("priorResultId").GetString() != call.Id)
            || (revise && data.GetProperty("recalculationReason").GetString() != input!.Value.GetProperty("reason").GetString()!.Trim())
            || (create && (data.GetProperty("revision").GetInt32() != 1 || data.GetProperty("rootResultId").GetString() != id)))
            throw new InvalidOperationException("Performance response identity or concurrency receipt does not match the request.");
        if (create && !MatchesPerformanceQuery(input!.Value, report.GetProperty("query")))
            throw new InvalidOperationException("Performance response query does not match the submitted selection.");
        return new((int)response.StatusCode, etag, response.Headers.Location?.ToString(), body);
    }

    private static bool IsStrongPerformanceETag(string? value) =>
        EntityTagHeaderValue.TryParse(value, out var tag) && !tag.IsWeak && tag.Tag != "*";

    private static bool MatchesPerformanceQuery(JsonElement expected, JsonElement actual)
    {
        // The producer normalizes set ordering and UTC offsets; this compares selection only, never financial values.
        foreach (var property in expected.EnumerateObject())
        {
            if (!actual.TryGetProperty(property.Name, out var value)) return false;
            if (property.Name is "walletIds" or "strategyVersionIds" or "instrumentIds" or "primaryTimeframeIds"
                && property.Value.ValueKind == JsonValueKind.Null && value.ValueKind == JsonValueKind.Array
                && value.GetArrayLength() == 0) continue;
            if (property.Value.ValueKind != value.ValueKind) return false;
            if (value.ValueKind == JsonValueKind.Array)
            {
                if (!property.Value.EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(value.EnumerateArray().Select(x => x.GetString()))) return false;
            }
            else if (property.Name is "fromUtc" or "toUtc")
            {
                if (property.Value.GetDateTimeOffset() != value.GetDateTimeOffset()) return false;
            }
            else if (value.ValueKind == JsonValueKind.String ? property.Value.GetString() != value.GetString() : property.Value.GetRawText() != value.GetRawText()) return false;
        }
        return true;
    }

    private static async Task<HttpRequestException> PerformanceConflictAsync(HttpResponseMessage response, CancellationToken token)
    {
        const string fallback = "Performance revision is stale (412). Read the current result before choosing a new revision.";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
            var root = document.RootElement;
            var id = root.GetProperty("currentResultId").GetString();
            var etag = root.GetProperty("currentETag").GetString();
            var revision = root.GetProperty("currentRevision").GetInt32();
            HeaderToken(id, "Current result identifier", 200);
            HeaderToken(etag, "Current ETag", 200);
            var path = "/api/v2/performance/results/" + Segment(id);
            if (revision < 1 || !IsStrongPerformanceETag(etag) || root.GetProperty("currentResultUrl").GetString() != path)
                return new(fallback, null, HttpStatusCode.PreconditionFailed);
            return new($"Performance revision is stale (412). Current result: {id}; revision: {revision}; ETag: {etag}; URL: {path}. Read this result before choosing a new revision.", null, HttpStatusCode.PreconditionFailed);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return new(fallback, null, HttpStatusCode.PreconditionFailed); }
    }
}
