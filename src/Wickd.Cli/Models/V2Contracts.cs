using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;

namespace Wickd.Cli.Models;

/// <summary>A schema-typed remote operation; request bytes are retained for exact retries.</summary>
public enum V2Operation
{
    CatalogAssets, CatalogInstruments, CatalogSpecs, CatalogTimeframes,
    MarketSnapshotGet, MarketSeriesCoverage,
    WalletCreate, WalletList, WalletGet, WalletBalance,
    RiskPreview, SetupAccept, SetupList, SetupGet, SetupAmend,
    SetupSkip, SetupMissed, SetupCancel, SetupNoteAppend, SetupNoteRevise,
    SetupAttemptCreate, SetupAttemptList, SetupAttemptGet,
    PaperFillCreate, PaperFillList, PaperFillGet, PaperFillCorrect,
    PaperHistoryCreate, PaperHistoryList, PaperHistoryGet, PaperHistoryCorrect,
    WalletAccountingCreate, WalletAccountingList, WalletAccountingGet
}

public sealed record V2Call(V2Operation Operation, string? Id = null, byte[]? Request = null,
    string? IdempotencyKey = null, string? IfMatch = null, int Limit = 50, string? Cursor = null,
    string? SubId = null, string? InstrumentId = null);

/// <summary>Exact server payload plus HTTP concurrency metadata. No financial parsing or calculation.</summary>
public sealed record V2Response(int Status, string? ETag, string? Location, JsonElement Body);

public static class V2Contracts
{
    private static readonly ConcurrentDictionary<string, JsonSchema> Schemas = new(StringComparer.Ordinal);

    public static JsonElement Validate(ReadOnlyMemory<byte> bytes, string contract)
    {
        using var document = JsonDocument.Parse(bytes);
        var schema = Schemas.GetOrAdd(contract, name =>
        {
            using var stream = typeof(V2Contracts).Assembly.GetManifestResourceStream($"Contracts.{name}.v2.schema.json")
                ?? throw new InvalidOperationException($"Missing bundled contract {name}.");
            using var reader = new StreamReader(stream);
            return JsonSchema.FromText(reader.ReadToEnd());
        });
        if (!schema.Evaluate(document.RootElement, new EvaluationOptions { RequireFormatValidation = true }).IsValid)
            throw new ArgumentException($"JSON does not satisfy the {contract} v2 contract. Check required properties, enum values, dates and decimal strings.");
        RejectDuplicateProperties(document.RootElement);
        return document.RootElement.Clone();
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new ArgumentException("Duplicate JSON properties are not accepted.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
