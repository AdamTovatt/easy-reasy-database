using System.Text.Json;

namespace EasyReasy.Database.Logging.Serialization
{
    /// <summary>
    /// Single source of truth for the JSON write contract on the operational log table's
    /// <c>properties</c> column. camelCase keys match conventional JSON API/DTO shapes so a future
    /// consumer of the column doesn't have to translate keys. Covers both
    /// <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> (anonymous-object / record property
    /// names) and <see cref="JsonSerializerOptions.DictionaryKeyPolicy"/> (the nested dictionaries
    /// Serilog's structure/dictionary rendering builds).
    /// </summary>
    internal static class LoggingJsonOptions
    {
        public static readonly JsonSerializerOptions Properties = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        };
    }
}
