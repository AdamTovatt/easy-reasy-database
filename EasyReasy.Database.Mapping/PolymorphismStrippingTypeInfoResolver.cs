using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Wraps an inner <see cref="IJsonTypeInfoResolver"/> and clears
    /// <see cref="JsonTypeInfo.PolymorphismOptions"/> on the returned <see cref="JsonTypeInfo"/>
    /// for any registered base type. Used by
    /// <see cref="OrderInsensitivePolymorphicJsonConverter{TBase}.Configure(JsonSerializerOptions)"/>
    /// when the consumer's options instance already has a custom resolver
    /// (e.g. a source-generated <c>JsonSerializerContext</c>) that the modifier-list approach
    /// can't reach.
    /// </summary>
    /// <remarks>
    /// One instance per <see cref="JsonSerializerOptions"/> (idempotent across multiple
    /// <c>Configure</c> calls) — additional base types are accumulated into the same
    /// <see cref="HashSet{T}"/> rather than stacking new wrappers.
    /// </remarks>
    internal sealed class PolymorphismStrippingTypeInfoResolver : IJsonTypeInfoResolver
    {
        private readonly IJsonTypeInfoResolver _inner;
        private readonly HashSet<Type> _strippedTypes = new HashSet<Type>();

        public PolymorphismStrippingTypeInfoResolver(IJsonTypeInfoResolver inner)
        {
            _inner = inner;
        }

        public void StripPolymorphismFor(Type type)
        {
            _strippedTypes.Add(type);
        }

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            JsonTypeInfo? info = _inner.GetTypeInfo(type, options);
            if (info != null && _strippedTypes.Contains(type) && info.PolymorphismOptions != null)
            {
                info.PolymorphismOptions = null;
            }
            return info;
        }
    }
}
