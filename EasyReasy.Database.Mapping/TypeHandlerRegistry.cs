using System.Collections.Concurrent;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Global registry for custom type handlers. Unlike Dapper, this registry is consulted
    /// for enum types before any default conversion, which is the core fix for the enum
    /// type handler bypass issue.
    /// </summary>
    public static class TypeHandlerRegistry
    {
        private static readonly ConcurrentDictionary<Type, ITypeHandler> Handlers = new();
        private static volatile int _version;

        /// <summary>
        /// Monotonically increasing version number, incremented whenever handlers are
        /// added or removed. Used by RowDeserializer to invalidate cached mappings.
        /// </summary>
        internal static int Version => _version;

        /// <summary>
        /// Registers a strongly-typed handler for type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type to handle.</typeparam>
        /// <param name="handler">The handler instance.</param>
        public static void AddTypeHandler<T>(TypeHandler<T> handler)
        {
            Handlers[typeof(T)] = handler;
            Interlocked.Increment(ref _version);
        }

        /// <summary>
        /// Registers a handler for the specified type.
        /// </summary>
        /// <param name="type">The type to handle.</param>
        /// <param name="handler">The handler instance.</param>
        public static void AddTypeHandler(Type type, ITypeHandler handler)
        {
            Handlers[type] = handler;
            Interlocked.Increment(ref _version);
        }

        /// <summary>
        /// Tries to get a registered handler for the specified type.
        /// </summary>
        /// <param name="type">The type to look up.</param>
        /// <param name="handler">The handler if found.</param>
        /// <returns>True if a handler was found.</returns>
        internal static bool TryGetHandler(Type type, out ITypeHandler? handler)
        {
            return Handlers.TryGetValue(type, out handler);
        }

        /// <summary>
        /// Removes all registered handlers. Intended for test cleanup.
        /// </summary>
        internal static void Clear()
        {
            Handlers.Clear();
            Interlocked.Increment(ref _version);
        }
    }
}
