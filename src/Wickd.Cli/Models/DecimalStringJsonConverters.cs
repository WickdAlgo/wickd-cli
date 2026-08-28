using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wickd.Cli.Models;

public sealed class DecimalStringJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !decimal.TryParse(reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new JsonException("Expected an invariant-culture decimal string.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

public sealed class NullableDecimalStringJsonConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String
            || !decimal.TryParse(reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            throw new JsonException("Expected null or an invariant-culture decimal string.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
    }
}
