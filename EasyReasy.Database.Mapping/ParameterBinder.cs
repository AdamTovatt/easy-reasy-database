using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Binds parameter objects to DbCommand parameters. Accepts anonymous objects (reflected),
    /// <see cref="DynamicParameters"/> bags, and any <see cref="IDictionary"/> (including
    /// generic Dictionary&lt;string, T&gt; variants via the non-generic interface). Rejects
    /// string-keyed dictionary-like types that don't implement <see cref="IDictionary"/> so
    /// they don't fall through to reflection and silently bind container properties as SQL params.
    /// Checks TypeHandlerRegistry for all values (including enums) before default conversion —
    /// the core fix for the Dapper enum type handler bypass.
    /// </summary>
    internal static class ParameterBinder
    {
        private static readonly ConcurrentDictionary<Type, bool> StringKeyedDictionaryLookupCache = new();

        /// <summary>
        /// Adds parameters to the command from <paramref name="param"/>. Accepts a
        /// <see cref="DynamicParameters"/> bag, any <see cref="IDictionary"/> (including generic
        /// <c>Dictionary&lt;string, T&gt;</c>), or an anonymous object whose public properties become
        /// named parameters. Null is a no-op. Throws <see cref="ArgumentException"/> for string-keyed
        /// dictionary-shaped types that don't implement <see cref="IDictionary"/>.
        /// </summary>
        internal static void BindParameters(DbCommand command, object? param)
        {
            if (param == null)
            {
                return;
            }

            if (param is DynamicParameters dynamicParams)
            {
                BindDynamicParameters(command, dynamicParams);
                return;
            }

            if (param is IDictionary dictionary)
            {
                BindDictionary(command, dictionary);
                return;
            }

            // `param is IEnumerable` is a cheap pre-filter: anonymous objects don't implement
            // IEnumerable, so this short-circuits before paying the (cached but first-time
            // reflection-heavy) interface walk in IsStringKeyedDictionaryShape.
            if (param is IEnumerable && IsStringKeyedDictionaryShape(param.GetType()))
            {
                throw new ArgumentException(
                    $"Parameter object of type '{param.GetType()}' looks like a string-keyed dictionary " +
                    $"but does not implement IDictionary. Pass an anonymous object, a DynamicParameters " +
                    $"instance, or a dictionary type that implements IDictionary (e.g. Dictionary<string, T>).",
                    nameof(param));
            }

            PropertyInfo[] properties = ReflectionCache.GetProperties(param.GetType());

            foreach (PropertyInfo property in properties)
            {
                object? value = property.GetValue(param);
                DbParameter dbParameter = command.CreateParameter();
                dbParameter.ParameterName = property.Name;

                if (value == null)
                {
                    dbParameter.Value = DBNull.Value;
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                Type valueType = value.GetType();

                // Check type handler registry first — this is the key difference from Dapper.
                // Dapper skips this check for enum types and converts them to integers instead.
                if (TypeHandlerRegistry.TryGetHandler(valueType, out ITypeHandler? handler) && handler != null)
                {
                    handler.SetValue(dbParameter, value);
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                // Handle arrays (used for ANY(@param) in PostgreSQL queries)
                if (valueType.IsArray && valueType != typeof(byte[]))
                {
                    dbParameter.Value = value;
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                // Default: set the value directly, letting the ADO.NET provider handle conversion
                dbParameter.Value = value;
                command.Parameters.Add(dbParameter);
            }
        }

        private static void BindDictionary(DbCommand command, IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string name)
                {
                    throw new ArgumentException(
                        $"Dictionary parameter must use string keys; found key of type '{entry.Key.GetType()}'.",
                        nameof(dictionary));
                }

                DbParameter dbParameter = command.CreateParameter();
                dbParameter.ParameterName = name;

                if (entry.Value == null)
                {
                    dbParameter.Value = DBNull.Value;
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                Type valueType = entry.Value.GetType();

                if (TypeHandlerRegistry.TryGetHandler(valueType, out ITypeHandler? handler) && handler != null)
                {
                    handler.SetValue(dbParameter, entry.Value);
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                if (valueType.IsArray && valueType != typeof(byte[]))
                {
                    dbParameter.Value = entry.Value;
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                dbParameter.Value = entry.Value;
                command.Parameters.Add(dbParameter);
            }
        }

        /// <summary>
        /// True when the type implements <see cref="IDictionary{TKey, TValue}"/> or
        /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> with a string key. Result is cached per type.
        /// The non-generic-IDictionary check is performed at the call site as a positive routing decision;
        /// this helper is used afterward to detect look-alike containers that should be rejected.
        /// </summary>
        private static bool IsStringKeyedDictionaryShape(Type type)
        {
            return StringKeyedDictionaryLookupCache.GetOrAdd(type, static t =>
            {
                foreach (Type iface in t.GetInterfaces())
                {
                    if (!iface.IsGenericType)
                    {
                        continue;
                    }

                    Type definition = iface.GetGenericTypeDefinition();
                    if (definition != typeof(IDictionary<,>) && definition != typeof(IReadOnlyDictionary<,>))
                    {
                        continue;
                    }

                    if (iface.GetGenericArguments()[0] == typeof(string))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        private static void BindDynamicParameters(DbCommand command, DynamicParameters dynamicParams)
        {
            foreach ((string name, object? value) in dynamicParams.GetParameters())
            {
                DbParameter dbParameter = command.CreateParameter();
                dbParameter.ParameterName = name;

                if (value == null)
                {
                    dbParameter.Value = DBNull.Value;
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                Type valueType = value.GetType();

                if (TypeHandlerRegistry.TryGetHandler(valueType, out ITypeHandler? handler) && handler != null)
                {
                    handler.SetValue(dbParameter, value);
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                if (valueType.IsArray && valueType != typeof(byte[]))
                {
                    dbParameter.Value = value;
                    command.Parameters.Add(dbParameter);
                    continue;
                }

                dbParameter.Value = value;
                command.Parameters.Add(dbParameter);
            }
        }
    }
}
