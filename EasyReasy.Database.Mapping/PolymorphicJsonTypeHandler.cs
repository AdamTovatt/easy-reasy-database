using System.Data;
using System.Text.Json;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// A <see cref="TypeHandler{T}"/> that serializes and deserializes
    /// <c>[JsonPolymorphic]</c> values using
    /// <see cref="OrderInsensitivePolymorphicJsonConverter{TBase}"/>, so reads work
    /// regardless of where the discriminator property appears in the JSON object.
    /// See the project README for usage and rationale.
    /// </summary>
    /// <typeparam name="TBase">The polymorphic base type. Must be annotated with <c>[JsonPolymorphic]</c>.</typeparam>
    public sealed class PolymorphicJsonTypeHandler<TBase> : TypeHandler<TBase>
        where TBase : class
    {
        private readonly JsonSerializerOptions _options;

        /// <summary>
        /// Creates a handler that serializes and deserializes <typeparamref name="TBase"/>
        /// values as JSON.
        /// </summary>
        /// <param name="options">
        /// Optional serializer options. The supplied instance is copied so the caller's
        /// options remain mutable; the copy is configured via
        /// <see cref="OrderInsensitivePolymorphicJsonConverter{TBase}.Configure(JsonSerializerOptions)"/>
        /// and then frozen via <see cref="JsonSerializerOptions.MakeReadOnly()"/>.
        /// When <c>null</c>, a default options instance is used.
        /// </param>
        public PolymorphicJsonTypeHandler(JsonSerializerOptions? options = null)
        {
            JsonSerializerOptions effective = options != null
                ? new JsonSerializerOptions(options)
                : new JsonSerializerOptions();

            OrderInsensitivePolymorphicJsonConverter<TBase>.Configure(effective);

            effective.MakeReadOnly();
            _options = effective;
        }

        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, TBase value)
        {
            parameter.Value = JsonSerializer.Serialize(value, _options);
            parameter.DbType = DbType.String;
        }

        /// <inheritdoc />
        public override TBase? Parse(object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value is string stringValue)
            {
                return JsonSerializer.Deserialize<TBase>(stringValue, _options);
            }

            return JsonSerializer.Deserialize<TBase>(value.ToString()!, _options);
        }
    }
}
