using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// A <see cref="JsonConverter{T}"/> that deserializes <c>[JsonPolymorphic]</c> hierarchies
    /// regardless of where the discriminator property appears in the JSON object — fixing the
    /// silent failures that occur when PostgreSQL JSONB reorders keys length-first then
    /// lexicographically. See the project README for the full rationale, performance notes,
    /// and usage examples.
    /// </summary>
    /// <remarks>
    /// Use <see cref="Configure(JsonSerializerOptions)"/> to register the converter; adding it
    /// directly to <see cref="JsonSerializerOptions.Converters"/> is not enough because
    /// <see cref="System.Text.Json"/> rejects custom converters on <c>[JsonPolymorphic]</c>
    /// types. Configuration is not thread-safe; call it once at startup.
    /// </remarks>
    /// <typeparam name="TBase">The polymorphic base type. Must be annotated with <c>[JsonPolymorphic]</c> and one or more <c>[JsonDerivedType]</c> attributes carrying explicit discriminators.</typeparam>
    public sealed class OrderInsensitivePolymorphicJsonConverter<TBase> : JsonConverter<TBase>
        where TBase : class
    {
        // PolymorphismMetadataCache.GetOrAdd already caches successes thread-safely and
        // naturally retries on failure (no entry is inserted when the factory throws), so
        // the indirection of a Lazy<T> here would only add value-less wrapping plus a
        // permanent failure cache — both undesirable.
        private static PolymorphismMetadata Metadata => PolymorphismMetadataCache.Get(typeof(TBase));

        private static readonly Action<JsonTypeInfo> _stripPolymorphism = StripPolymorphism;

        /// <summary>
        /// Adds this converter to <paramref name="options"/> and arranges for
        /// <see cref="JsonTypeInfo.PolymorphismOptions"/> to be cleared for
        /// <typeparamref name="TBase"/>, so this converter (rather than
        /// <see cref="System.Text.Json"/>'s built-in polymorphism flow) handles dispatch.
        /// Idempotent. Not thread-safe.
        /// </summary>
        /// <param name="options">The options instance to configure. Must not be read-only.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
        public static void Configure(JsonSerializerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            EnsureTypeInfoModifier(options);
            EnsureConverter(options);
        }

        private static void EnsureConverter(JsonSerializerOptions options)
        {
            foreach (JsonConverter existing in options.Converters)
            {
                if (existing is OrderInsensitivePolymorphicJsonConverter<TBase>)
                {
                    return;
                }
            }
            options.Converters.Add(new OrderInsensitivePolymorphicJsonConverter<TBase>());
        }

        private static void EnsureTypeInfoModifier(JsonSerializerOptions options)
        {
            if (options.TypeInfoResolver is DefaultJsonTypeInfoResolver defaultResolver)
            {
                AddModifier(defaultResolver);
                return;
            }

            if (options.TypeInfoResolver == null)
            {
                DefaultJsonTypeInfoResolver fresh = new DefaultJsonTypeInfoResolver();
                AddModifier(fresh);
                options.TypeInfoResolver = fresh;
                return;
            }

            // Custom resolver (e.g. a JsonSerializerContext from source generation): can't add
            // a modifier to it, so wrap it. A single wrapper is reused across Configure calls
            // (and across TBase values) — additional types are accumulated into its set rather
            // than stacking nested wrappers. Source-generated JsonTypeInfo may be read-only and
            // reject the PolymorphismOptions assignment, in which case STJ throws
            // InvalidOperationException with its own (generic) message.
            if (options.TypeInfoResolver is PolymorphismStrippingTypeInfoResolver existing)
            {
                existing.StripPolymorphismFor(typeof(TBase));
                return;
            }

            PolymorphismStrippingTypeInfoResolver wrapper = new PolymorphismStrippingTypeInfoResolver(options.TypeInfoResolver);
            wrapper.StripPolymorphismFor(typeof(TBase));
            options.TypeInfoResolver = wrapper;
        }

        private static void AddModifier(DefaultJsonTypeInfoResolver resolver)
        {
            if (resolver.Modifiers.Contains(_stripPolymorphism))
            {
                return;
            }
            resolver.Modifiers.Add(_stripPolymorphism);
        }

        private static void StripPolymorphism(JsonTypeInfo typeInfo)
        {
            if (typeInfo.Type == typeof(TBase) && typeInfo.PolymorphismOptions != null)
            {
                typeInfo.PolymorphismOptions = null;
            }
        }

        /// <inheritdoc />
        public override TBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Expected StartObject when deserializing '{typeof(TBase).Name}', got {reader.TokenType}.");
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;

            string discriminatorPropertyName = Metadata.DiscriminatorPropertyName;
            if (!TryGetDiscriminator(root, discriminatorPropertyName, options.PropertyNameCaseInsensitive, out JsonElement discriminator))
            {
                throw new JsonException(
                    $"Discriminator property '{discriminatorPropertyName}' not found while deserializing '{typeof(TBase).Name}'.");
            }

            Type? derivedType = ResolveDerivedType(discriminator);
            if (derivedType == null)
            {
                throw new JsonException(
                    $"Discriminator value '{discriminator.ToString()}' did not match any [JsonDerivedType] registered on '{typeof(TBase).Name}'.");
            }

            if (derivedType == typeof(TBase))
            {
                // Would re-enter this converter and stack-overflow. Configuration error.
                throw new JsonException(
                    $"Discriminator '{discriminator.ToString()}' resolved to '{typeof(TBase).Name}' itself, which is not supported (would recurse). Remove [JsonDerivedType(typeof({typeof(TBase).Name}), ...)] from the base type.");
            }

            return (TBase?)root.Deserialize(derivedType, options);
        }

        /// <summary>
        /// Serializes <paramref name="value"/> with the discriminator written first, so the
        /// output is interoperable with default <see cref="System.Text.Json"/> deserialization.
        /// Runtime types that are not registered as a <c>[JsonDerivedType]</c> on
        /// <typeparamref name="TBase"/> (e.g. grandchild subclasses) are serialized by the
        /// default path with no discriminator.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            Type runtimeType = value.GetType();

            if (runtimeType == typeof(TBase))
            {
                // The polymorphic base itself is being serialized, not a derived instance.
                // Serializing as runtimeType would re-enter this converter and stack-overflow.
                throw new JsonException(
                    $"Cannot serialize an instance of the polymorphic base type '{typeof(TBase).Name}' directly. Either make the base type abstract or use a registered [JsonDerivedType].");
            }

            object? discriminatorValue = ResolveDiscriminator(runtimeType);

            if (discriminatorValue == null)
            {
                // Runtime type not registered as a derived type. Serialize as the runtime
                // type so we don't recurse back into this converter.
                JsonSerializer.Serialize(writer, value, runtimeType, options);
                return;
            }

            // Serialize as the runtime type into a temporary document (bypasses this converter
            // because it is registered for TBase, not for the derived type), then emit a fresh
            // object with the discriminator written first.
            using JsonDocument document = JsonSerializer.SerializeToDocument(value, runtimeType, options);

            writer.WriteStartObject();

            string discriminatorName = Metadata.DiscriminatorPropertyName;
            switch (discriminatorValue)
            {
                case string s:
                    writer.WriteString(discriminatorName, s);
                    break;
                case int i:
                    writer.WriteNumber(discriminatorName, i);
                    break;
                default:
                    throw new JsonException(
                        $"Unsupported discriminator value type '{discriminatorValue.GetType().Name}' for '{typeof(TBase).Name}'. Only string and int are supported.");
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        private static bool TryGetDiscriminator(JsonElement root, string discriminatorName, bool caseInsensitive, out JsonElement value)
        {
            // Fast path: case-sensitive lookup uses JsonElement's internal name index.
            if (!caseInsensitive)
            {
                return root.TryGetProperty(discriminatorName, out value);
            }

            // Case-insensitive parity with JsonSerializerOptions.PropertyNameCaseInsensitive,
            // which STJ's built-in polymorphism honors when matching the discriminator key.
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, discriminatorName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static Type? ResolveDerivedType(JsonElement discriminator)
        {
            foreach ((object expected, Type derivedType) in Metadata.DerivedTypes)
            {
                if (expected is string expectedString && discriminator.ValueKind == JsonValueKind.String)
                {
                    if (discriminator.ValueEquals(expectedString))
                    {
                        return derivedType;
                    }
                }
                else if (expected is int expectedInt && discriminator.ValueKind == JsonValueKind.Number)
                {
                    if (discriminator.TryGetInt32(out int actual) && actual == expectedInt)
                    {
                        return derivedType;
                    }
                }
            }

            return null;
        }

        private static object? ResolveDiscriminator(Type runtimeType)
        {
            foreach ((object expected, Type derivedType) in Metadata.DerivedTypes)
            {
                if (derivedType == runtimeType)
                {
                    return expected;
                }
            }
            return null;
        }
    }
}
