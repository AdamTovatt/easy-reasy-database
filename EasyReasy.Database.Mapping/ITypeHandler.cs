using System.Data;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Interface for custom type handlers that control how values are read from and written to database parameters.
    /// </summary>
    public interface ITypeHandler
    {
        /// <summary>
        /// Sets the value of a database parameter.
        /// </summary>
        /// <param name="parameter">The parameter to configure.</param>
        /// <param name="value">The value to set.</param>
        void SetValue(IDbDataParameter parameter, object value);

        /// <summary>
        /// Parses a value read from the database into the destination type.
        /// </summary>
        /// <param name="destinationType">The target CLR type.</param>
        /// <param name="value">The raw value from the database reader.</param>
        /// <returns>The parsed value.</returns>
        object? Parse(Type destinationType, object value);
    }
}
