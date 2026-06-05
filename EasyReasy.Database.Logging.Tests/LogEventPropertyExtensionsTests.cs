using System.Text.Json;
using EasyReasy.Database.Logging.Serialization;
using Serilog.Events;
using Serilog.Parsing;

namespace EasyReasy.Database.Logging.Tests
{
    /// <summary>
    /// Covers <see cref="LogEventPropertyExtensions.SerializeRemainingProperties"/> for the non-scalar
    /// Serilog value shapes — sequences, structures, dictionaries — which the JSON catch-all must
    /// render as native nested JSON rather than quoted <c>ToString()</c> output.
    /// </summary>
    public class LogEventPropertyExtensionsTests
    {
        private static readonly IReadOnlySet<string> NoKnownColumns = new HashSet<string>();

        private static LogEvent EventWith(params LogEventProperty[] properties)
        {
            MessageTemplate parsed = new MessageTemplateParser().Parse("test");
            return new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, exception: null, parsed, properties);
        }

        [Fact]
        public void SerializeRemainingProperties_WithSequence_RendersJsonArray()
        {
            LogEvent logEvent = EventWith(new LogEventProperty("Numbers", new SequenceValue(new[]
            {
                new ScalarValue(1),
                new ScalarValue(2),
                new ScalarValue(3),
            })));

            string? json = logEvent.SerializeRemainingProperties(NoKnownColumns);

            Assert.NotNull(json);
            using JsonDocument document = JsonDocument.Parse(json!);
            JsonElement numbers = document.RootElement.GetProperty("numbers");
            Assert.Equal(JsonValueKind.Array, numbers.ValueKind);
            Assert.Equal(new[] { 1, 2, 3 }, numbers.EnumerateArray().Select(element => element.GetInt32()).ToArray());
        }

        [Fact]
        public void SerializeRemainingProperties_WithStructure_RendersNestedObjectWithCamelCaseKeys()
        {
            LogEvent logEvent = EventWith(new LogEventProperty("Address", new StructureValue(new[]
            {
                new LogEventProperty("City", new ScalarValue("Stockholm")),
                new LogEventProperty("ZipCode", new ScalarValue("11122")),
            })));

            string? json = logEvent.SerializeRemainingProperties(NoKnownColumns);

            Assert.NotNull(json);
            using JsonDocument document = JsonDocument.Parse(json!);
            JsonElement address = document.RootElement.GetProperty("address");
            Assert.Equal(JsonValueKind.Object, address.ValueKind);
            Assert.Equal("Stockholm", address.GetProperty("city").GetString());
            Assert.Equal("11122", address.GetProperty("zipCode").GetString());
        }

        [Fact]
        public void SerializeRemainingProperties_WithDictionary_RendersJsonObject()
        {
            LogEvent logEvent = EventWith(new LogEventProperty("Counts", new DictionaryValue(new[]
            {
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("hits"), new ScalarValue(10)),
                new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("misses"), new ScalarValue(2)),
            })));

            string? json = logEvent.SerializeRemainingProperties(NoKnownColumns);

            Assert.NotNull(json);
            using JsonDocument document = JsonDocument.Parse(json!);
            JsonElement counts = document.RootElement.GetProperty("counts");
            Assert.Equal(JsonValueKind.Object, counts.ValueKind);
            Assert.Equal(10, counts.GetProperty("hits").GetInt32());
            Assert.Equal(2, counts.GetProperty("misses").GetInt32());
        }

        [Fact]
        public void SerializeRemainingProperties_WithNoUnknownProperties_ReturnsNull()
        {
            LogEvent logEvent = EventWith(new LogEventProperty("SourceContext", new ScalarValue("App")));
            IReadOnlySet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SourceContext" };

            Assert.Null(logEvent.SerializeRemainingProperties(known));
        }
    }
}
