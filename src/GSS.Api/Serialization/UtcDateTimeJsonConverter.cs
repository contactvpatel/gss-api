using System.Text.Json;
using System.Text.Json.Serialization;
using GSS.Util.Time;

namespace GSS.Api.Serialization;

/// <summary>Reads every <see cref="DateTime"/> as UTC (no offset = UTC) and writes it with a <c>Z</c> suffix.</summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    /// <inheritdoc />
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return UtcDateTime.Normalize(reader.GetDateTime());
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(UtcDateTime.Normalize(value));
    }
}
