using System.Data;
using System.Text.Json;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// A <see cref="TypeHandler{T}"/> that serializes and deserializes plain
    /// JSON-backed values: POCOs, records, dictionaries, lists — anything that
    /// round-trips through <see cref="JsonSerializer"/> with default behavior.
    /// For polymorphic hierarchies (types annotated with <c>[JsonPolymorphic]</c>)
    /// prefer <see cref="PolymorphicJsonTypeHandler{TBase}"/>, which additionally
    /// handles the JSONB key-reordering issue with the discriminator.
    /// See the project README for usage and rationale.
    /// </summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    public sealed class JsonTypeHandler<T> : TypeHandler<T>
    {
        private readonly JsonSerializerOptions? _options;

        /// <summary>
        /// Creates a handler that serializes and deserializes <typeparamref name="T"/>
        /// values as JSON.
        /// </summary>
        /// <param name="options">
        /// Optional serializer options. When <c>null</c>, <see cref="System.Text.Json"/>
        /// defaults are used. The instance is used as supplied — no internal copy or
        /// freeze — so caller mutations after construction take effect on subsequent
        /// reads/writes.
        /// </param>
        public JsonTypeHandler(JsonSerializerOptions? options = null)
        {
            _options = options;
        }

        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, T value)
        {
            parameter.Value = JsonSerializer.Serialize(value, _options);
            parameter.DbType = DbType.String;
        }

        /// <inheritdoc />
        public override T? Parse(object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value is string stringValue)
            {
                return JsonSerializer.Deserialize<T>(stringValue, _options);
            }

            return JsonSerializer.Deserialize<T>(value.ToString()!, _options);
        }
    }
}
