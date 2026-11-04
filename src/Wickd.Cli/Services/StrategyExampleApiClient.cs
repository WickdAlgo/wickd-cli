using System.Net.Http.Headers;
using System.Text.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient
{
    private static bool IsStrategyExampleOperation(V2Operation operation) => operation is
        V2Operation.StrategyExampleCreate or V2Operation.StrategyExampleList or
        V2Operation.StrategyExampleGet or V2Operation.StrategyExampleEvidence;

    private async Task<V2Response> SendStrategyExampleAsync(V2Call call, CancellationToken token)
    {
        RequireV2Credentials(requireWorkspace: true);
        var category = call.ExampleCategory ?? "decisionOnly";
        if (category is not ("decisionOnly" or "outcomeExample"))
            throw new ArgumentException("Example category must be decisionOnly or outcomeExample.");
        var evidence = call.Operation == V2Operation.StrategyExampleEvidence;
        if (evidence && category != "decisionOnly")
            throw new ArgumentException("Decision evidence accepts decisionOnly examples.");
        var route = $"strategies/{Segment(call.Id)}/examples";
        var contract = "strategy-example-reference";
        var method = HttpMethod.Get;
        if (call.Operation == V2Operation.StrategyExampleCreate) method = HttpMethod.Post;
        else if (call.Operation == V2Operation.StrategyExampleList)
        {
            if (call.Limit is < 1 or > 200) throw new ArgumentException("Example page limit must be between 1 and 200.");
            route += $"?limit={call.Limit}&category={category}";
            contract = "strategy-example-page";
        }
        else
        {
            route += "/" + Segment(call.SubId);
            route += evidence ? "/decision-evidence" : "?category=" + category;
            if (evidence) contract = "setup-research-example";
        }
        if (call.Cursor is not null)
        {
            if (method == HttpMethod.Post) throw new ArgumentException("Create does not accept a read cursor.");
            ValidateCursor(call.Cursor);
            route += (route.Contains('?') ? "&" : "?") + "cursor=" + Uri.EscapeDataString(call.Cursor);
        }
        JsonElement? expectedSource = null;
        if (evidence)
        {
            var reference = await SendStrategyExampleAsync(call with { Operation = V2Operation.StrategyExampleGet }, token);
            expectedSource = reference.Body.GetProperty("data");
        }
        using var request = new HttpRequestMessage(method, "api/v2/" + route);
        JsonElement? input = null;
        if (method == HttpMethod.Post)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            if (call.Request is null || call.Request.Length is 0 or > 65_536)
                throw new ArgumentException("An example request of at most 64 KiB is required.");
            input = V2Contracts.Validate(call.Request, "strategy-example-create-request");
            request.Headers.Add("Idempotency-Key", call.IdempotencyKey);
            request.Content = new ByteArrayContent(call.Request);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await _httpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Strategy-example API request failed with status {(int)response.StatusCode} ({response.StatusCode}).", null, response.StatusCode);
        JsonElement body;
        try { body = V2Contracts.Validate(await response.Content.ReadAsByteArrayAsync(token), contract, evidence ? 1 : 2); }
        catch (Exception error) when (error is ArgumentException or JsonException)
        { throw new InvalidOperationException("Unsupported or malformed strategy-example response: " + error.Message, error); }
        if (body.GetProperty("schemaVersion").GetInt32() != (evidence ? 1 : 2)
            || body.GetProperty("contract").GetString() != contract)
            throw new InvalidOperationException("Unsupported strategy-example contract or version.");
        if (expectedSource is { } source && (body.GetProperty("snapshotId").GetString() != source.GetProperty("snapshotId").GetString()
            || body.GetProperty("observation").GetProperty("observationId").GetString() != source.GetProperty("observationId").GetString()))
            throw new InvalidOperationException("Decision evidence does not match the exact strategy-example source.");
        if (!evidence)
        {
            var data = body.GetProperty("data");
            void Check(JsonElement item)
            {
                var expectedCategory = input?.GetProperty("category").GetString() ?? category;
                if (item.GetProperty("strategyId").GetString() != call.Id
                    || item.GetProperty("category").GetString() != expectedCategory
                    || item.GetProperty("association").GetString() != "humanIllustrative"
                    || (call.Operation == V2Operation.StrategyExampleGet && item.GetProperty("exampleId").GetString() != call.SubId))
                    throw new InvalidOperationException("Strategy-example response identity or category does not match the requested resource.");
                if (input is { } submitted)
                    foreach (var property in new[] { "versionId", "snapshotId", "observationId", "annotation" })
                        if (item.GetProperty(property).GetString() != submitted.GetProperty(property).GetString())
                            throw new InvalidOperationException("Strategy-example response does not match the submitted reference.");
            }
            if (call.Operation == V2Operation.StrategyExampleList)
                foreach (var item in data.GetProperty("items").EnumerateArray()) Check(item);
            else Check(data);
        }
        var etag = response.Headers.ETag?.ToString();
        if (call.Operation is V2Operation.StrategyExampleCreate or V2Operation.StrategyExampleGet
            && (!EntityTagHeaderValue.TryParse(etag, out var tag) || tag.IsWeak || tag.Tag == "*"))
            throw new InvalidOperationException("Strategy-example response omitted its strong ETag.");
        return new((int)response.StatusCode, etag, response.Headers.Location?.ToString(), body);
    }
}
