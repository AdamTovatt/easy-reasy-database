namespace EasyReasy.Database.Sorting
{
    /// <summary>
    /// Marks a DTO property as sortable in queries.
    /// Optionally specifies a custom SQL column name, otherwise auto-generates snake_case from property name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class SortableAttribute : Attribute
    {
        /// <summary>
        /// Optional custom SQL column name. If not specified, snake_case will be auto-generated from the property name.
        /// </summary>
        public string? ColumnName { get; }

        /// <summary>
        /// Whether this property should be used as the default sort column when no sort is specified.
        /// </summary>
        public bool IsDefault { get; }

        /// <summary>
        /// Marks a property as sortable.
        /// </summary>
        /// <param name="columnName">Optional custom SQL column name. If null, snake_case will be auto-generated.</param>
        /// <param name="isDefault">Whether this is the default sort column.</param>
        public SortableAttribute(string? columnName = null, bool isDefault = false)
        {
            ColumnName = columnName;
            IsDefault = isDefault;
        }
    }
}

