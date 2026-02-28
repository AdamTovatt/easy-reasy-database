using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Maps DbDataReader rows to CLR objects using reflection. Checks TypeHandlerRegistry
    /// for property types (including enums) before applying default conversion.
    /// </summary>
    internal static class RowDeserializer
    {
        /// <summary>
        /// Cache key combining the reader's column schema with the target entity type.
        /// The column names string is built once per unique set of result columns.
        /// </summary>
        private static readonly ConcurrentDictionary<(string ColumnNames, Type EntityType), (PropertyInfo Property, int Ordinal)[]> MappingCache = new();

        /// <summary>
        /// Cached GetFieldValue method info for reflection-based typed reading.
        /// </summary>
        private static readonly MethodInfo GetFieldValueMethod =
            typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetFieldValue))!;

        private static readonly ConcurrentDictionary<Type, MethodInfo> GetFieldValueCache = new();

        /// <summary>
        /// Deserializes all rows from the reader into a list of <typeparamref name="T"/>.
        /// </summary>
        internal static async Task<List<T>> DeserializeAsync<T>(DbDataReader reader)
        {
            List<T> results = new List<T>();

            if (!reader.HasRows)
            {
                return results;
            }

            (PropertyInfo Property, int Ordinal)[] mapping = GetMapping<T>(reader);

            while (await reader.ReadAsync())
            {
                T instance = CreateInstance<T>(reader, mapping);
                results.Add(instance);
            }

            return results;
        }

        /// <summary>
        /// Reads a single scalar value from the reader.
        /// </summary>
        internal static async Task<T?> ReadScalarAsync<T>(DbDataReader reader)
        {
            if (!await reader.ReadAsync())
            {
                return default;
            }

            if (reader.IsDBNull(0))
            {
                return default;
            }

            object value = reader.GetValue(0);

            return ConvertValue<T>(value, typeof(T));
        }

        /// <summary>
        /// Creates a single instance of <typeparamref name="T"/> from the current reader row.
        /// </summary>
        private static T CreateInstance<T>(DbDataReader reader, (PropertyInfo Property, int Ordinal)[] mapping)
        {
            T instance = Activator.CreateInstance<T>();

            foreach ((PropertyInfo property, int ordinal) in mapping)
            {
                if (reader.IsDBNull(ordinal))
                {
                    continue;
                }

                Type targetType = property.PropertyType;
                Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                // Check type handler registry first — including for enum types.
                // When a handler is registered, read the value as a string so the handler's
                // Parse method receives the database string representation (e.g. "active").
                // This avoids Npgsql's GetValue()/GetFieldValue<T>() issues with mapped enums.
                if (TypeHandlerRegistry.TryGetHandler(underlyingType, out ITypeHandler? handler) && handler != null)
                {
                    string stringValue = reader.GetFieldValue<string>(ordinal);
                    object? parsed = handler.Parse(underlyingType, stringValue);
                    property.SetValue(instance, parsed);
                    continue;
                }

                // For enum types mapped via Npgsql's MapEnum<T>, use GetFieldValue<T>()
                // which returns the C# enum directly. GetValue() fails for Npgsql-mapped enums
                // because the provider can't box them to object via the untyped API.
                if (underlyingType.IsEnum)
                {
                    object enumValue = ReadFieldValue(reader, ordinal, underlyingType);
                    property.SetValue(instance, enumValue);
                    continue;
                }

                // DateOnly/TimeOnly: GetValue() returns DateTime/TimeSpan,
                // but GetFieldValue<T>() returns the correct types via Npgsql.
                if (underlyingType == typeof(DateOnly) || underlyingType == typeof(TimeOnly))
                {
                    object fieldValue = ReadFieldValue(reader, ordinal, underlyingType);
                    property.SetValue(instance, fieldValue);
                    continue;
                }

                // For all other types, GetValue() works fine
                {
                    object value = reader.GetValue(ordinal);

                    if (value.GetType() == underlyingType)
                    {
                        property.SetValue(instance, value);
                    }
                    else
                    {
                        property.SetValue(instance, Convert.ChangeType(value, underlyingType));
                    }
                }
            }

            return instance;
        }

        /// <summary>
        /// Reads a field value using the typed GetFieldValue&lt;T&gt; method via reflection.
        /// This is necessary for Npgsql-mapped enum types where GetValue() fails.
        /// </summary>
        private static object ReadFieldValue(DbDataReader reader, int ordinal, Type fieldType)
        {
            MethodInfo typedMethod = GetFieldValueCache.GetOrAdd(fieldType,
                t => GetFieldValueMethod.MakeGenericMethod(t));

            return typedMethod.Invoke(reader, new object[] { ordinal })!;
        }

        /// <summary>
        /// Converts a scalar value to the target type.
        /// </summary>
        private static T? ConvertValue<T>(object value, Type targetType)
        {
            Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // Check handler first
            if (TypeHandlerRegistry.TryGetHandler(underlyingType, out ITypeHandler? handler) && handler != null)
            {
                return (T?)handler.Parse(underlyingType, value);
            }

            // Enum handling
            if (underlyingType.IsEnum)
            {
                if (value is string stringValue)
                {
                    return (T)Enum.Parse(underlyingType, stringValue, ignoreCase: true);
                }
                return (T)Enum.ToObject(underlyingType, value);
            }

            // Direct cast if types match
            if (value is T typedValue)
            {
                return typedValue;
            }

            // DateOnly/TimeOnly: GetValue() returns DateTime/TimeSpan
            if (underlyingType == typeof(DateOnly) && value is DateTime dateTime)
            {
                return (T)(object)DateOnly.FromDateTime(dateTime);
            }

            if (underlyingType == typeof(TimeOnly) && value is TimeSpan timeSpan)
            {
                return (T)(object)TimeOnly.FromTimeSpan(timeSpan);
            }

            // Convert
            return (T)Convert.ChangeType(value, underlyingType);
        }

        /// <summary>
        /// Gets or creates the column-to-property mapping for the given reader and entity type.
        /// </summary>
        private static (PropertyInfo Property, int Ordinal)[] GetMapping<T>(DbDataReader reader)
        {
            string columnNames = BuildColumnNamesKey(reader);
            Type entityType = typeof(T);

            return MappingCache.GetOrAdd((columnNames, entityType), _ =>
            {
                PropertyInfo[] properties = ReflectionCache.GetProperties(entityType);
                List<(PropertyInfo, int)> mappings = new List<(PropertyInfo, int)>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string columnName = reader.GetName(i);

                    foreach (PropertyInfo property in properties)
                    {
                        if (string.Equals(property.Name, columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            mappings.Add((property, i));
                            break;
                        }
                    }
                }

                return mappings.ToArray();
            });
        }

        /// <summary>
        /// Builds a cache key string from the reader's column names.
        /// </summary>
        private static string BuildColumnNamesKey(DbDataReader reader)
        {
            string[] columns = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns[i] = reader.GetName(i);
            }
            return string.Join(",", columns);
        }
    }
}
