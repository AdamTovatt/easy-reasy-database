namespace EasyReasy.Database.Sorting
{
    /// <summary>
    /// Represents a validated sort column for a DTO property.
    /// Can only be created through the Create method to ensure validation.
    /// </summary>
    public class SortColumn
    {
        /// <summary>
        /// The property name that this sort column represents.
        /// </summary>
        public string PropertyName { get; }

        /// <summary>
        /// The SQL column name or expression to use in ORDER BY clauses.
        /// </summary>
        public string SqlColumnName { get; }

        /// <summary>
        /// Internal constructor. Use Create to create instances.
        /// </summary>
        internal SortColumn(string propertyName, string sqlColumnName)
        {
            PropertyName = propertyName;
            SqlColumnName = sqlColumnName;
        }

        /// <summary>
        /// Creates a validated SortColumn from a property name.
        /// Validates that the property exists and is marked as sortable.
        /// </summary>
        /// <typeparam name="TDto">The DTO type to validate against.</typeparam>
        /// <param name="propertyName">The property name to create a SortColumn for.</param>
        /// <returns>A validated SortColumn struct.</returns>
        /// <exception cref="ArgumentException">Thrown when the property doesn't exist or isn't marked as sortable.</exception>
        public static SortColumn Create<TDto>(string propertyName)
        {
            string sqlColumnName = SortableFieldHelper.GetSqlColumnName<TDto>(propertyName);
            return new SortColumn(propertyName, sqlColumnName);
        }
    }
}

