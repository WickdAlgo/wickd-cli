using System.Net.Http.Headers;
using System.Text.Json;
using Wickd.Cli.Models;

namespace Wickd.Cli.Services;

public sealed partial class WickdApiClient
{
    private static bool IsStrategyContextOperation(V2Operation operation) => operation is
        V2Operation.SessionDefinitionCreate or V2Operation.SessionDefinitionVersionCreate or
        V2Operation.SessionDefinitionVersionList or V2Operation.SessionDefinitionVersionGet or
        V2Operation.MarketConditionCreate or V2Operation.MarketConditionRevisionCreate or
        V2Operation.MarketConditionRevisionList or V2Operation.MarketConditionRevisionGet or
        V2Operation.StrategyScopeGet or V2Operation.StrategyScopeUpdate or V2Operation.StrategyScopeList or
        V2Operation.StrategyContextScopeGet or V2Operation.StrategyContextScopeUpdate or
        V2Operation.SetupContextObservationCreate or V2Operation.SetupContextObservationList or V2Operation.SetupContextGet;

    private async Task<V2Response> SendStrategyContextAsync(V2Call call, CancellationToken cancellationToken)
    {
        var (route, responseContract, requestContract, method) = call.Operation switch
        {
            V2Operation.SessionDefinitionCreate => ("session-definitions", "session-definition-version", "session-definition-request", HttpMethod.Post),
            V2Operation.SessionDefinitionVersionCreate => ($"session-definitions/{Segment(call.Id)}/versions", "session-definition-version", "session-definition-request", HttpMethod.Post),
            V2Operation.SessionDefinitionVersionList => ("session-definition-versions", "session-definition-page", (string?)null, HttpMethod.Get),
            V2Operation.SessionDefinitionVersionGet => ($"session-definition-versions/{Segment(call.Id)}", "session-definition-version", (string?)null, HttpMethod.Get),
            V2Operation.MarketConditionCreate => ("market-condition-tags", "market-condition-revision", "market-condition-request", HttpMethod.Post),
            V2Operation.MarketConditionRevisionCreate => ($"market-condition-tags/{Segment(call.Id)}/revisions", "market-condition-revision", "market-condition-request", HttpMethod.Post),
            V2Operation.MarketConditionRevisionList => ("market-condition-revisions", "market-condition-page", (string?)null, HttpMethod.Get),
            V2Operation.MarketConditionRevisionGet => ($"market-condition-revisions/{Segment(call.Id)}", "market-condition-revision", (string?)null, HttpMethod.Get),
            V2Operation.StrategyScopeGet => ($"strategy-versions/{Segment(call.Id)}/scope", "strategy-version-scope", (string?)null, HttpMethod.Get),
            V2Operation.StrategyScopeUpdate => ($"strategy-versions/{Segment(call.Id)}/scope", "strategy-version-scope", "strategy-scope-update-request", HttpMethod.Patch),
            V2Operation.StrategyScopeList => ($"strategies/{Segment(call.Id)}/version-scopes", "strategy-scope-list-page", (string?)null, HttpMethod.Get),
            V2Operation.StrategyContextScopeGet => ($"strategy-versions/{Segment(call.Id)}/context-scope", "strategy-context-scope", (string?)null, HttpMethod.Get),
            V2Operation.StrategyContextScopeUpdate => ($"strategy-versions/{Segment(call.Id)}/context-scope", "strategy-context-scope", "strategy-context-scope-request", HttpMethod.Patch),
            V2Operation.SetupContextObservationCreate => ($"setups/{Segment(call.Id)}/context-observations", "setup-context-observation", "setup-context-request", HttpMethod.Post),
            V2Operation.SetupContextObservationList => ($"setups/{Segment(call.Id)}/context-observations", "setup-context-observation-page", (string?)null, HttpMethod.Get),
            V2Operation.SetupContextGet => ($"setups/{Segment(call.Id)}/context", "setup-context-view", (string?)null, HttpMethod.Get),
            _ => throw new ArgumentException("Unknown strategy-context operation.")
        };

        RequireV2Credentials(requireWorkspace: true);
        var isPage = call.Operation is V2Operation.SessionDefinitionVersionList or V2Operation.MarketConditionRevisionList
            or V2Operation.StrategyScopeList or V2Operation.SetupContextObservationList;
        if (isPage)
        {
            if (call.Limit is < 1 or > 200) throw new ArgumentException("Page limit must be between 1 and 200.");
            route += $"?limit={call.Limit}";
        }

        if (call.Cursor is not null)
        {
            ValidateCursor(call.Cursor);
            route += (route.Contains('?') ? "&" : "?") + "cursor=" + Uri.EscapeDataString(call.Cursor);
        }

        if (call.Operation == V2Operation.SetupContextGet)
            route = AppendSetupContextFilters(route, call.SessionDefinitionVersionId, call.MarketConditionTagId);

        using var request = new HttpRequestMessage(method, "api/v2/" + route);
        if (requestContract is not null)
        {
            HeaderToken(call.IdempotencyKey, "Idempotency-Key", 200);
            if (call.Request is null || call.Request.Length is 0 or > 65_536)
                throw new ArgumentException("A JSON request of at most 64 KiB is required for strategy context writes.");
            _ = V2Contracts.Validate(call.Request, requestContract);
            request.Headers.Add("Idempotency-Key", call.IdempotencyKey);
            request.Content = new ByteArrayContent(call.Request);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        if (RequiresContextIfMatch(call.Operation))
        {
            HeaderToken(call.IfMatch, "If-Match", 200);
            if (!EntityTagHeaderValue.TryParse(call.IfMatch, out var tag) || tag.IsWeak || tag.Tag == "*")
                throw new ArgumentException("If-Match must be the current strong ETag for the resource being updated.");
            request.Headers.IfMatch.Add(tag);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Strategy-context API request failed with status {(int)response.StatusCode} ({response.StatusCode}).", null, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        JsonElement body;
        try { body = V2Contracts.Validate(bytes, responseContract); }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            throw new InvalidOperationException("Unsupported or malformed strategy-context API response: " + error.Message, error);
        }
        if (body.GetProperty("schemaVersion").GetInt32() != 2 || body.GetProperty("contract").GetString() != responseContract)
            throw new InvalidOperationException($"Unsupported API contract; expected {responseContract} v2.");
        ValidateContextIdentity(call, body.GetProperty("data"));
        var etag = response.Headers.ETag?.ToString();
        if (requestContract is not null
            && (!EntityTagHeaderValue.TryParse(etag, out var responseTag) || responseTag.IsWeak || responseTag.Tag == "*"))
            throw new InvalidOperationException("Strategy-context write response omitted its strong concurrency ETag.");
        return new V2Response((int)response.StatusCode, etag, response.Headers.Location?.ToString(), body);
    }

    private static bool RequiresContextIfMatch(V2Operation operation) => operation is
        V2Operation.SessionDefinitionVersionCreate or V2Operation.MarketConditionRevisionCreate or
        V2Operation.StrategyScopeUpdate or V2Operation.StrategyContextScopeUpdate or V2Operation.SetupContextObservationCreate;

    private static void ValidateCursor(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Trim() != cursor || cursor.Any(char.IsControl))
            throw new ArgumentException("Cursor must be a nonempty opaque value without surrounding whitespace or control characters.");
    }

    private static string AppendSetupContextFilters(string route, string? sessionVersionId, string? conditionTagId)
    {
        if (sessionVersionId is not null)
        {
            HeaderToken(sessionVersionId, "Session definition version ID", 200);
            route += (route.Contains('?') ? "&" : "?") + "sessionDefinitionVersionId=" + Uri.EscapeDataString(sessionVersionId);
        }
        if (conditionTagId is not null)
        {
            HeaderToken(conditionTagId, "Market condition tag ID", 200);
            route += (route.Contains('?') ? "&" : "?") + "marketConditionTagId=" + Uri.EscapeDataString(conditionTagId);
        }
        return route;
    }

    private static void ValidateContextIdentity(V2Call call, JsonElement data)
    {
        static string Required(JsonElement value, string property) => value.GetProperty(property).GetString()
            ?? throw new InvalidOperationException($"Strategy-context response omitted {property}.");
        void Matches(string property, string expected)
        {
            if (!string.Equals(Required(data, property), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Strategy-context response identity does not match the requested resource.");
        }

        switch (call.Operation)
        {
            case V2Operation.SessionDefinitionVersionCreate:
                Matches("sessionId", call.Id!);
                break;
            case V2Operation.SessionDefinitionVersionGet:
                Matches("versionId", call.Id!);
                break;
            case V2Operation.MarketConditionRevisionCreate:
                Matches("tagId", call.Id!);
                break;
            case V2Operation.MarketConditionRevisionGet:
                Matches("revisionId", call.Id!);
                break;
            case V2Operation.StrategyScopeGet:
            case V2Operation.StrategyScopeUpdate:
            case V2Operation.StrategyContextScopeGet:
            case V2Operation.StrategyContextScopeUpdate:
                Matches("versionId", call.Id!);
                break;
            case V2Operation.StrategyScopeList:
                if (data.GetProperty("items").EnumerateArray().Any(item => !string.Equals(item.GetProperty("strategyId").GetString(), call.Id, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Strategy-scope page contains a different strategy identity.");
                break;
            case V2Operation.SetupContextObservationCreate:
            case V2Operation.SetupContextGet:
                Matches("setupId", call.Id!);
                if (call.Operation == V2Operation.SetupContextGet
                    && data.TryGetProperty("observation", out var observation)
                    && observation.ValueKind == JsonValueKind.Object
                    && !string.Equals(observation.GetProperty("setupId").GetString(), call.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("Setup-context observation identity does not match the requested setup.");
                break;
            case V2Operation.SetupContextObservationList:
                if (data.GetProperty("items").EnumerateArray()
                    .Any(item => !string.Equals(item.GetProperty("setupId").GetString(), call.Id, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Setup-context history contains a different setup identity.");
                break;
        }
    }
}
