using System.Text.Json;
using System.Text.Json.Serialization;

namespace LPLK_Surf;

// Existing configuration writers can emit integral JSON numbers as 9876.0.
// Accept the same exact integer, never round fractions or coerce strings.
public sealed class IntegralInt32Converter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out decimal value)
            && value == decimal.Truncate(value) && value >= int.MinValue && value <= int.MaxValue)
            return (int)value;
        throw new JsonException("An exact Int32 numeric value is required.");
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
