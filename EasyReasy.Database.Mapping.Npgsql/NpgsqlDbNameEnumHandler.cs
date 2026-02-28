using System.Data;
using Npgsql;

namespace EasyReasy.Database.Mapping.Npgsql
{
    /// <summary>
    /// An Npgsql-specific enum handler that sets <see cref="NpgsqlParameter.DataTypeName"/>
    /// on the parameter, eliminating the need for <c>::pg_type</c> casts in SQL queries.
    /// </summary>
    /// <typeparam name="T">The enum type to handle.</typeparam>
    public class NpgsqlDbNameEnumHandler<T> : DbNameEnumHandler<T> where T : struct, Enum
    {
        private readonly string _pgTypeName;

        /// <summary>
        /// Creates a new handler that will set <see cref="NpgsqlParameter.DataTypeName"/>
        /// to the specified PostgreSQL enum type name.
        /// </summary>
        /// <param name="pgTypeName">The PostgreSQL enum type name (e.g. <c>"my_status"</c>).</param>
        public NpgsqlDbNameEnumHandler(string pgTypeName)
        {
            _pgTypeName = pgTypeName;
        }

        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, T value)
        {
            parameter.Value = _enumToString[value];

            if (parameter is NpgsqlParameter npgsqlParameter)
            {
                npgsqlParameter.DataTypeName = _pgTypeName;
            }
        }
    }
}
