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
        private readonly Dictionary<T, string> _enumToString;
        private readonly Dictionary<string, T> _stringToEnum;

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

        public override void SetValue(IDbDataParameter parameter, T value)
        {
            parameter.Value = _enumToString[value];
            parameter.DbType = DbType.String;
        }

        public override T Parse(object value)
        {
            return _stringToEnum[value.ToString()!];
        }
    }
}
