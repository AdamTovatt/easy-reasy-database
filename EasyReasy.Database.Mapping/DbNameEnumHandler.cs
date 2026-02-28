using System.Data;
using System.Reflection;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// A type handler that maps enum values to database strings using <see cref="DbNameAttribute"/>.
    /// </summary>
    /// <typeparam name="T">The enum type to handle.</typeparam>
    public class DbNameEnumHandler<T> : TypeHandler<T> where T : struct, Enum
    {
        /// <summary>Lookup from enum value to its database string representation.</summary>
        protected readonly Dictionary<T, string> _enumToString;

        /// <summary>Lookup from database string representation to enum value.</summary>
        protected readonly Dictionary<string, T> _stringToEnum;

        /// <summary>
        /// Builds the bidirectional enum-to-string lookup from <see cref="DbNameAttribute"/> on each field.
        /// Throws <see cref="InvalidOperationException"/> if any field is missing the attribute.
        /// </summary>
        public DbNameEnumHandler()
        {
            _enumToString = new Dictionary<T, string>();

            foreach (T value in Enum.GetValues<T>())
            {
                FieldInfo field = typeof(T).GetField(value.ToString())!;
                DbNameAttribute? attr = field.GetCustomAttribute<DbNameAttribute>();

                if (attr is null)
                {
                    throw new InvalidOperationException(
                        $"Enum field '{typeof(T).Name}.{value}' is missing a [DbName] attribute.");
                }

                _enumToString[value] = attr.Name;
            }

            _stringToEnum = _enumToString.ToDictionary(kv => kv.Value, kv => kv.Key);
        }

        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, T value)
        {
            parameter.Value = _enumToString[value];
            parameter.DbType = DbType.String;
        }

        /// <inheritdoc />
        public override T Parse(object value)
        {
            if (value is T enumValue)
            {
                return enumValue;
            }

            return _stringToEnum[value.ToString()!];
        }
    }
}
