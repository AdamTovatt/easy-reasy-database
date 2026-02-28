using System.Data;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Base class for strongly-typed custom type handlers.
    /// </summary>
    /// <typeparam name="T">The CLR type this handler manages.</typeparam>
    public abstract class TypeHandler<T> : ITypeHandler
    {
        /// <summary>
        /// Sets the value of a database parameter for type <typeparamref name="T"/>.
        /// </summary>
        /// <param name="parameter">The parameter to configure.</param>
        /// <param name="value">The typed value to set.</param>
        public abstract void SetValue(IDbDataParameter parameter, T value);

        /// <summary>
        /// Parses a raw database value into an instance of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="value">The raw value from the database reader.</param>
        /// <returns>The parsed value.</returns>
        public abstract T? Parse(object value);

        void ITypeHandler.SetValue(IDbDataParameter parameter, object value)
        {
            SetValue(parameter, (T)value);
        }

        object? ITypeHandler.Parse(Type destinationType, object value)
        {
            return Parse(value);
        }
    }
}
