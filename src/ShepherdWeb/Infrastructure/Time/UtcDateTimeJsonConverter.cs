using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShepherdWeb.Infrastructure.Time;

/// <summary>
/// Applied to every generated ShepherdAPI client. Reads: timestamps without an offset are UTC
/// (F4); timestamps with an offset are converted to UTC. Writes: always ISO 8601 with "Z", after
/// <see cref="Utc.ToApi"/> has refused unspecified-kind values.
/// Also applies to DateTime? — System.Text.Json wraps DateTime converters for nullable values.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("Expected a timestamp string.");
        // AssumeUniversal: no offset means UTC. AdjustToUniversal: an explicit offset is converted.
        // Either way the result has Kind=Utc.
        return DateTime.Parse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Utc.ToApi(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
}
