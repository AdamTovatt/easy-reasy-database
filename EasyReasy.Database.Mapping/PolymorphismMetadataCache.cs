using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Process-wide cache of <see cref="PolymorphismMetadata"/>, keyed by base type.
    /// Each entry is built once via reflection on <c>[JsonPolymorphic]</c> /
    /// <c>[JsonDerivedType]</c> attributes.
    /// </summary>
    internal static class PolymorphismMetadataCache
    {
        // The discriminator property name System.Text.Json uses when [JsonPolymorphic]
        // does not specify one.
        private const string DefaultDiscriminatorPropertyName = "$type";

        private static readonly ConcurrentDictionary<Type, PolymorphismMetadata> _cache = new();

        public static PolymorphismMetadata Get(Type baseType)
        {
            return _cache.GetOrAdd(baseType, Build);
        }

        private static PolymorphismMetadata Build(Type baseType)
        {
            JsonPolymorphicAttribute? polyAttr = baseType.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false);
            if (polyAttr == null)
            {
                throw new InvalidOperationException(
                    $"Type '{baseType.Name}' is not annotated with [JsonPolymorphic]; the order-insensitive polymorphic converter cannot be applied.");
            }

            string discriminatorName = string.IsNullOrEmpty(polyAttr.TypeDiscriminatorPropertyName)
                ? DefaultDiscriminatorPropertyName
                : polyAttr.TypeDiscriminatorPropertyName!;

            JsonDerivedTypeAttribute[] derivedAttrs = (JsonDerivedTypeAttribute[])baseType.GetCustomAttributes(typeof(JsonDerivedTypeAttribute), inherit: false);

            List<(object Discriminator, Type DerivedType)> derivedTypes = new List<(object, Type)>(derivedAttrs.Length);
            foreach (JsonDerivedTypeAttribute attr in derivedAttrs)
            {
                if (attr.TypeDiscriminator == null)
                {
                    throw new InvalidOperationException(
                        $"Type '{baseType.Name}' has a [JsonDerivedType(typeof({attr.DerivedType.Name}))] attribute without an explicit discriminator. " +
                        $"The order-insensitive polymorphic converter requires every [JsonDerivedType] to specify a string or int discriminator.");
                }
                derivedTypes.Add((attr.TypeDiscriminator, attr.DerivedType));
            }

            return new PolymorphismMetadata(discriminatorName, derivedTypes);
        }
    }
}
