using System.Collections.Concurrent;
using System.Reflection;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Caches reflection metadata (property accessors, setters, column mappings) to avoid
    /// repeated reflection lookups on every query.
    /// </summary>
    internal static class ReflectionCache
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

        /// <summary>
        /// Gets the public instance properties of a type, cached for reuse.
        /// </summary>
        internal static PropertyInfo[] GetProperties(Type type)
        {
            return PropertyCache.GetOrAdd(type, t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        }
    }
}
