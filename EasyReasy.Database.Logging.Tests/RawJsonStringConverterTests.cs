using System.Text.Json;
using EasyReasy.Database.Logging.Models;

namespace EasyReasy.Database.Logging.Tests
{
    /// <summary>
    /// Pins the wire contract for the operational log <c>Properties</c> blob: the JSON-in-string
    /// value must serialize as a real nested object (written verbatim) rather than an escaped string
    /// literal, and must round-trip back to its raw text.
    /// </summary>
    public class RawJsonStringConverterTests
    {
        private static readonly JsonSerializerOptions CamelCase = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        [Fact]
        public void Serialize_WritesPropertiesVerbatim_NotAsEscapedString()
        {
            OperationalLogEntry entry = new OperationalLogEntry
            {
                Level = "Information",
                Message = "user logged in",
                Properties = "{\"action\":\"login\",\"attempts\":3}",
            };

            string json = JsonSerializer.Serialize(entry, CamelCase);

            // The blob appears verbatim as a nested object…
            Assert.Contains("\"properties\":{\"action\":\"login\",\"attempts\":3}", json);
            // …and never as a double-encoded string literal.
            Assert.DoesNotContain("\\\"action\\\"", json);
        }

        [Fact]
        public void Serialize_NullProperties_EmitsJsonNull()
        {
            OperationalLogEntry entry = new OperationalLogEntry
            {
                Level = "Information",
                Message = "no properties",
                Properties = null,
            };

            string json = JsonSerializer.Serialize(entry, CamelCase);

            Assert.Contains("\"properties\":null", json);
        }

        [Fact]
        public void RoundTrip_PreservesRawJsonText()
        {
            OperationalLogEntry original = new OperationalLogEntry
            {
                Level = "Warning",
                Message = "nested",
                Properties = "{\"outer\":{\"inner\":[1,2,3]},\"flag\":true}",
            };

            string json = JsonSerializer.Serialize(original);
            OperationalLogEntry? roundTripped = JsonSerializer.Deserialize<OperationalLogEntry>(json);

            Assert.NotNull(roundTripped);
            // GetRawText() drops insignificant whitespace but preserves structure and key order.
            using JsonDocument expected = JsonDocument.Parse(original.Properties);
            using JsonDocument actual = JsonDocument.Parse(roundTripped!.Properties!);
            Assert.Equal(expected.RootElement.GetRawText(), actual.RootElement.GetRawText());
        }

        [Fact]
        public void Serialize_RespectsCamelCaseStreamOptions_LikeTheSseFeed()
        {
            // Mirrors the SSE endpoint, which serializes events with a camelCase options instance.
            JsonSerializerOptions streamOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            OperationalLogEvent logEvent = new OperationalLogEvent
            {
                Level = "Information",
                Message = "streamed",
                Properties = "{\"action\":\"login\"}",
            };

            string json = JsonSerializer.Serialize(logEvent, streamOptions);

            // Property-level converter is honored regardless of the options instance.
            Assert.Contains("\"properties\":{\"action\":\"login\"}", json);
        }
    }
}
