namespace EasyReasy.Database.Sorting
{
    /// <summary>
    /// Helper class for building ORDER BY clauses from sort specifications.
    /// </summary>
    public static class OrderBy
    {
        /// <summary>
        /// Creates an ORDER BY clause for a DTO type.
        /// If sortColumn is provided, uses that column's SQL column name.
        /// If sortColumn is null, uses the default sort column from the DTO (if one exists).
        /// Returns an empty string if no sort column is available.
        /// </summary>
        /// <typeparam name="TDto">The DTO type to get sort information from.</typeparam>
        /// <param name="sortColumn">The validated sort column to sort by, or null to use the default.</param>
        /// <param name="sortOrder">The sort direction.</param>
        /// <returns>An ORDER BY clause string, or empty string if no sorting is available.</returns>
        public static string Create<TDto>(SortColumn? sortColumn, SortOrder sortOrder)
        {
            string? sqlColumnName = null;

            if (sortColumn is not null)
            {
                sqlColumnName = sortColumn.SqlColumnName;
            }
            else
            {
                sqlColumnName = SortableFieldHelper.GetDefaultSortColumn<TDto>();
            }

            return SortableFieldHelper.BuildOrderByClause(sqlColumnName, sortOrder);
        }
    }
}

