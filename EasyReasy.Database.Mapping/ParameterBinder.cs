using System.Collections;
using System.Data.Common;
using System.Reflection;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Binds anonymous object properties to DbCommand parameters. Checks TypeHandlerRegistry
    /// for all types (including enums) before applying default conversion — this is the core
    /// fix for the Dapper enum type handler bypass.
    /// </summary>
    internal static class ParameterBinder
    {
        /// <summary>
        /// Adds parameters from an anonymous object to the command.
        /// </summary>
        internal static void BindParameters(DbCommand command, object? param)
        {
            if (param == null)
            {
                return;
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
    }
}
