using System.Reflection;
using Npgsql;

namespace EasyReasy.Database.Mapping.Npgsql
{
    /// <summary>
    /// Extension methods for <see cref="NpgsqlDataSourceBuilder"/> to simplify
    /// PostgreSQL enum type registration with EasyReasy.Database.Mapping.
    /// </summary>
    public static class NpgsqlDataSourceBuilderExtensions
    {
        /// <summary>
        /// Registers a PostgreSQL enum type mapping for <typeparamref name="T"/> using
        /// the type name from its <see cref="DbEnumAttribute"/> and a
        /// <see cref="NpgsqlDbNameEnumHandler{T}"/> for parameter binding.
        /// </summary>
        /// <remarks>
        /// This is equivalent to calling both <c>builder.MapEnum&lt;T&gt;(pgTypeName)</c>
        /// and <c>TypeHandlerRegistry.AddTypeHandler(new NpgsqlDbNameEnumHandler&lt;T&gt;(pgTypeName))</c>,
        /// but reads the type name from the <see cref="DbEnumAttribute"/> on the enum type
        /// so it only needs to be specified once.
        /// </remarks>
        /// <typeparam name="T">The enum type to map. Must have a <see cref="DbEnumAttribute"/>
        /// and all fields must have <see cref="DbNameAttribute"/>.</typeparam>
        /// <returns>The builder, for fluent chaining.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <typeparamref name="T"/> is missing a <see cref="DbEnumAttribute"/>.
        /// </exception>
        public static NpgsqlDataSourceBuilder MapDbNameEnum<T>(this NpgsqlDataSourceBuilder builder)
            where T : struct, Enum
        {
            DbEnumAttribute attribute = typeof(T).GetCustomAttribute<DbEnumAttribute>()
                ?? throw new InvalidOperationException(
                    $"Enum type '{typeof(T).Name}' is missing a [DbEnum] attribute.");

            builder.MapEnum<T>(attribute.Name);
            TypeHandlerRegistry.AddTypeHandler(new NpgsqlDbNameEnumHandler<T>(attribute.Name));

            return builder;
        }
    }
}
