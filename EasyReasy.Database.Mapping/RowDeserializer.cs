using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Maps DbDataReader rows to CLR objects using reflection. Checks TypeHandlerRegistry
    /// for property types (including enums) before applying default conversion.
    /// Supports automatic snake_case → PascalCase column mapping and constructor-based entity creation.
    /// </summary>
    internal static class RowDeserializer
    {
        /// <summary>
        /// Cache key combining the reader's column schema with the target entity type.
        /// The column names string is built once per unique set of result columns.
        /// </summary>
        private static readonly ConcurrentDictionary<(string ColumnNames, Type EntityType), EntityMapping> MappingCache = new();

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

            EntityMapping mapping = GetMapping<T>(reader);

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
        /// Uses either parameterless or parameterized constructor based on the entity's ConstructionStrategy.
        /// </summary>
        private static T CreateInstance<T>(DbDataReader reader, EntityMapping mapping)
        {
            object instance;
            ConstructionStrategy strategy = mapping.Strategy;

            if (strategy.HasParameterlessConstructor)
            {
                instance = strategy.ParameterlessFactory!();
            }
            else
            {
                // Build constructor arguments from pre-computed ordinal mappings
                ParameterInfo[] ctorParams = strategy.ConstructorParameters;
                object[] args = new object[ctorParams.Length];

                for (int i = 0; i < mapping.ConstructorArgMappings.Length; i++)
                {
                    ColumnMeta meta = mapping.ConstructorArgMappings[i];

                    if (meta.Ordinal < 0 || reader.IsDBNull(meta.Ordinal))
                    {
                        // No matching column or NULL → default(T) for the original parameter type
                        args[i] = GetDefaultValue(meta.OriginalType)!;
                    }
                    else
                    {
                        args[i] = ReadValue(reader, meta);
                    }
                }

                instance = strategy.ParameterizedFactory!(args);
            }

            // Set remaining properties via compiled setters
            foreach ((Action<object, object?> setter, ColumnMeta meta) in mapping.SetterMappings)
            {
                if (reader.IsDBNull(meta.Ordinal))
                {
                    continue;
                }

                object value = ReadValue(reader, meta);
                setter(instance, value);
            }

            return (T)instance;
        }

        /// <summary>
        /// Reads a value from the reader using pre-computed column metadata.
        /// All type classification and handler resolution is done once during mapping construction.
        /// </summary>
        private static object ReadValue(DbDataReader reader, ColumnMeta meta)
        {
            switch (meta.Kind)
            {
                case ColumnKind.Handler:
                    object rawValue = reader.GetValue(meta.Ordinal);
                    return meta.Handler!.Parse(meta.UnderlyingType, rawValue)!;

                case ColumnKind.Enum:
                case ColumnKind.DateOnly:
                case ColumnKind.TimeOnly:
                    return ReadFieldValue(reader, meta.Ordinal, meta.UnderlyingType);

                default:
                    object value = reader.GetValue(meta.Ordinal);
                    if (value.GetType() == meta.UnderlyingType)
                    {
                        return value;
                    }
                    return Convert.ChangeType(value, meta.UnderlyingType);
            }
        }

        /// <summary>
        /// Returns the default value for a type (null for reference/nullable types, default(T) for value types).
        /// </summary>
        private static object? GetDefaultValue(Type type)
        {
            if (!type.IsValueType)
            {
                return null;
            }

            // For nullable value types, default is null
            if (Nullable.GetUnderlyingType(type) != null)
            {
                return null;
            }

            // For non-nullable value types, create default via Activator
            return Activator.CreateInstance(type);
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
        /// Gets or creates the entity mapping for the given reader and entity type.
        /// </summary>
        private static EntityMapping GetMapping<T>(DbDataReader reader)
        {
            string columnNames = BuildColumnNamesKey(reader);
            Type entityType = typeof(T);
            var cacheKey = (columnNames, entityType);
            int currentVersion = TypeHandlerRegistry.Version;

            // Check if the cached mapping is stale due to handler registry changes.
            if (MappingCache.TryGetValue(cacheKey, out EntityMapping? cached) && cached.HandlerVersion == currentVersion)
            {
                return cached;
            }

            // Build (or rebuild) the mapping with the current handler state.
            EntityMapping mapping = BuildMapping(reader, entityType, currentVersion);
            MappingCache[cacheKey] = mapping;
            return mapping;
        }

        private static EntityMapping BuildMapping(DbDataReader reader, Type entityType, int handlerVersion)
        {
            PropertyInfo[] properties = ReflectionCache.GetProperties(entityType);
            ConstructionStrategy strategy = ReflectionCache.GetConstructionStrategy(entityType);

            // Build a map: PascalCase property name → ordinal (trying direct match, then snake_case conversion)
            Dictionary<string, int> propertyOrdinals = new(StringComparer.OrdinalIgnoreCase);

            // First, try to match all columns to properties
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string columnName = reader.GetName(i);

                // Direct case-insensitive match against properties
                foreach (PropertyInfo prop in properties)
                {
                    if (string.Equals(prop.Name, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        propertyOrdinals.TryAdd(prop.Name, i);
                        break;
                    }
                }

                // If no direct match, try snake_case → PascalCase conversion
                if (!propertyOrdinals.Values.Contains(i))
                {
                    string pascalName = SnakeCaseToPascalCase(columnName);
                    foreach (PropertyInfo prop in properties)
                    {
                        if (string.Equals(prop.Name, pascalName, StringComparison.OrdinalIgnoreCase))
                        {
                            propertyOrdinals.TryAdd(prop.Name, i);
                            break;
                        }
                    }
                }
            }

            // Build setter mappings for settable properties
            List<(Action<object, object?>, ColumnMeta)> setterMappings = new();
            foreach (PropertyInfo prop in strategy.SettableProperties)
            {
                if (propertyOrdinals.TryGetValue(prop.Name, out int ordinal))
                {
                    Action<object, object?> setter = ReflectionCache.GetPropertySetter(prop);
                    setterMappings.Add((setter, ResolveColumnMeta(ordinal, prop.PropertyType)));
                }
            }

            // Build constructor arg mappings (only for parameterized path)
            ColumnMeta[]? ctorArgMappings = null;
            if (!strategy.HasParameterlessConstructor)
            {
                ctorArgMappings = new ColumnMeta[strategy.ConstructorParameters.Length];
                for (int i = 0; i < strategy.ConstructorParameters.Length; i++)
                {
                    ParameterInfo param = strategy.ConstructorParameters[i];
                    int ordinal = -1;

                    if (param.Name != null && propertyOrdinals.TryGetValue(param.Name, out int resolved))
                    {
                        ordinal = resolved;
                    }

                    ctorArgMappings[i] = ResolveColumnMeta(ordinal, param.ParameterType);
                }
            }

            return new EntityMapping(
                strategy,
                setterMappings.ToArray(),
                ctorArgMappings ?? Array.Empty<ColumnMeta>(),
                handlerVersion);
        }

        /// <summary>
        /// Converts a snake_case column name to PascalCase.
        /// Fast path for names without underscores (just capitalizes first letter).
        /// </summary>
        internal static string SnakeCaseToPascalCase(string snakeCase)
        {
            if (string.IsNullOrEmpty(snakeCase))
            {
                return snakeCase;
            }

            // Fast path: no underscores — just capitalize first letter
            if (snakeCase.IndexOf('_') < 0)
            {
                if (char.IsUpper(snakeCase[0]))
                {
                    return snakeCase;
                }

                Span<char> buffer = stackalloc char[snakeCase.Length];
                snakeCase.AsSpan().CopyTo(buffer);
                buffer[0] = char.ToUpperInvariant(buffer[0]);
                return new string(buffer);
            }

            // General path: split on underscores, capitalize each segment
            Span<char> result = stackalloc char[snakeCase.Length];
            int writeIndex = 0;
            bool capitalizeNext = true;

            for (int i = 0; i < snakeCase.Length; i++)
            {
                char c = snakeCase[i];

                if (c == '_')
                {
                    capitalizeNext = true;
                    continue;
                }

                if (capitalizeNext)
                {
                    result[writeIndex++] = char.ToUpperInvariant(c);
                    capitalizeNext = false;
                }
                else
                {
                    result[writeIndex++] = c;
                }
            }

            return new string(result[..writeIndex]);
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

        /// <summary>
        /// Pre-computes column metadata (underlying type, kind, handler) so that
        /// ReadValue can use a simple switch instead of per-row type analysis.
        /// </summary>
        private static ColumnMeta ResolveColumnMeta(int ordinal, Type propertyType)
        {
            Type underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (TypeHandlerRegistry.TryGetHandler(underlyingType, out ITypeHandler? handler) && handler != null)
            {
                return new ColumnMeta(ordinal, propertyType, underlyingType, ColumnKind.Handler, handler);
            }

            if (underlyingType.IsEnum)
            {
                return new ColumnMeta(ordinal, propertyType, underlyingType, ColumnKind.Enum, null);
            }

            if (underlyingType == typeof(DateOnly))
            {
                return new ColumnMeta(ordinal, propertyType, underlyingType, ColumnKind.DateOnly, null);
            }

            if (underlyingType == typeof(TimeOnly))
            {
                return new ColumnMeta(ordinal, propertyType, underlyingType, ColumnKind.TimeOnly, null);
            }

            return new ColumnMeta(ordinal, propertyType, underlyingType, ColumnKind.Default, null);
        }
    }

    /// <summary>
    /// Classifies how a column should be read from the DbDataReader.
    /// </summary>
    internal enum ColumnKind
    {
        Default,
        Handler,
        Enum,
        DateOnly,
        TimeOnly
    }

    /// <summary>
    /// Pre-computed metadata for a single column, resolved once during mapping
    /// and reused for every row without per-row type analysis.
    /// </summary>
    internal readonly struct ColumnMeta
    {
        public readonly int Ordinal;
        public readonly Type OriginalType;
        public readonly Type UnderlyingType;
        public readonly ColumnKind Kind;
        public readonly ITypeHandler? Handler;

        public ColumnMeta(int ordinal, Type originalType, Type underlyingType, ColumnKind kind, ITypeHandler? handler)
        {
            Ordinal = ordinal;
            OriginalType = originalType;
            UnderlyingType = underlyingType;
            Kind = kind;
            Handler = handler;
        }
    }

    /// <summary>
    /// Pre-computed mapping for a specific (column set, entity type) pair.
    /// Eliminates per-row dictionary lookups by pre-resolving all ordinals, type metadata, and delegates.
    /// </summary>
    internal sealed class EntityMapping
    {
        /// <summary>
        /// The construction strategy for the entity type.
        /// </summary>
        public ConstructionStrategy Strategy { get; }

        /// <summary>
        /// Pre-computed setter mappings: (compiled setter delegate, column metadata).
        /// Used for settable properties (all props for parameterless ctor, remaining props for parameterized ctor).
        /// </summary>
        public (Action<object, object?> Setter, ColumnMeta Meta)[] SetterMappings { get; }

        /// <summary>
        /// Pre-computed constructor argument metadata.
        /// Parallel to ConstructionStrategy.ConstructorParameters.
        /// Ordinal is -1 when no matching column was found (default value will be used).
        /// Empty for parameterless constructor path.
        /// </summary>
        public ColumnMeta[] ConstructorArgMappings { get; }

        /// <summary>
        /// The TypeHandlerRegistry version when this mapping was built.
        /// If the registry version has changed, this mapping is stale and must be rebuilt.
        /// </summary>
        public int HandlerVersion { get; }

        public EntityMapping(
            ConstructionStrategy strategy,
            (Action<object, object?> Setter, ColumnMeta Meta)[] setterMappings,
            ColumnMeta[] constructorArgMappings,
            int handlerVersion)
        {
            Strategy = strategy;
            SetterMappings = setterMappings;
            ConstructorArgMappings = constructorArgMappings;
            HandlerVersion = handlerVersion;
        }
    }
}
