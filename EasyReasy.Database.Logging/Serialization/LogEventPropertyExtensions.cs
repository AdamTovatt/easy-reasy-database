using System.Text.Json;
using Serilog.Events;

namespace EasyReasy.Database.Logging.Serialization
{
    /// <summary>
    /// Helpers for extracting scalar properties from a Serilog <see cref="LogEvent"/> and
    /// serialising the remainder into a JSON document suitable for the <c>properties</c> column.
    /// Shared by every sink so typed-column mapping versus the <c>properties</c> catch-all stays
    /// consistent across write paths.
    /// </summary>
    internal static class LogEventPropertyExtensions
    {
        /// <summary>
        /// Returns the value of a scalar property as a string, or <c>null</c> if the property is
        /// missing or not a <see cref="ScalarValue"/>. Non-scalar values (sequences, structures,
        /// dictionaries) intentionally return <c>null</c> so they flow into
        /// <see cref="SerializeRemainingProperties"/> as proper JSON rather than an ambiguous
        /// <c>ToString()</c>.
        /// </summary>
        public static string? GetScalarString(this LogEvent logEvent, string name)
        {
            if (!logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value))
            {
                return null;
            }

            if (value is ScalarValue scalar && scalar.Value != null)
            {
                return scalar.Value.ToString();
            }

            return null;
        }

        /// <summary>
        /// Serialises every property not listed in <paramref name="knownColumnProperties"/> into a
        /// single JSON document for the <c>properties</c> column. Keys are camelCased via
        /// <see cref="LoggingJsonOptions.Properties"/>. Sequences become JSON arrays, structures and
        /// dictionaries become JSON objects — never quoted <c>ToString()</c> output — so JSON-path
        /// queries work. Returns <c>null</c> when there is nothing to serialise.
        /// </summary>
        public static string? SerializeRemainingProperties(
            this LogEvent logEvent,
            IReadOnlySet<string> knownColumnProperties)
        {
            Dictionary<string, object?> remaining = new Dictionary<string, object?>();

            foreach (KeyValuePair<string, LogEventPropertyValue> kvp in logEvent.Properties)
            {
                if (knownColumnProperties.Contains(kvp.Key))
                {
                    continue;
                }

                remaining[kvp.Key] = RenderValue(kvp.Value);
            }

            if (remaining.Count == 0)
            {
                return null;
            }

            return JsonSerializer.Serialize(remaining, LoggingJsonOptions.Properties);
        }

        /// <summary>
        /// Recursively unwraps a Serilog <see cref="LogEventPropertyValue"/> into a CLR object graph
        /// that <see cref="JsonSerializer"/> renders as native JSON. Scalars become their underlying
        /// value, sequences become lists, structures and dictionaries become string-keyed
        /// dictionaries. Unknown value types fall back to <c>ToString()</c>.
        /// </summary>
        private static object? RenderValue(LogEventPropertyValue value)
        {
            switch (value)
            {
                case ScalarValue scalar:
                    return scalar.Value;

                case SequenceValue sequence:
                    return sequence.Elements.Select(RenderValue).ToList();

                case StructureValue structure:
                    return structure.Properties.ToDictionary(p => p.Name, p => RenderValue(p.Value));

                case DictionaryValue dictionary:
                    return dictionary.Elements.ToDictionary(
                        kvp => kvp.Key.Value?.ToString() ?? "null",
                        kvp => RenderValue(kvp.Value));

                default:
                    return value.ToString();
            }
        }
    }
}
