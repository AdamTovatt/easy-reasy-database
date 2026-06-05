using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyReasy.Database.Logging.Serialization
{
    /// <summary>
    /// Serializes a <see cref="string"/> property that already holds a JSON document by writing it
    /// to the output verbatim instead of as an escaped string literal, and reads it back as its raw
    /// JSON text. Applied to the operational log <c>Properties</c> blob so the catch-all JSON travels
    /// over the HTTP read and the SSE feed as a real nested object — not a double-encoded string that
    /// every client has to parse twice. <see cref="JsonConverter{T}.HandleNull"/> stays <c>false</c>,
    /// so a <c>null</c> property is emitted as JSON <c>null</c> without reaching this converter.
    /// </summary>
    public sealed class RawJsonStringConverter : JsonConverter<string>
    {
        /// <inheritdoc />
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.GetRawText();
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            // Invariant: this only ever sees the operational log Properties blob, which is written
            // exclusively by the sink (SerializeRemainingProperties) or read from the jsonb column —
            // so it is always either null (handled before reaching here) or a valid JSON document.
            // WriteRawValue validates and would throw on malformed/empty input; that can only happen
            // via out-of-band DB corruption or a manual insert, which we intentionally do not guard.
            writer.WriteRawValue(value);
        }
    }
}
