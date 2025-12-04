using System.Reflection;
using System.Text;

namespace EasyReasy.Database.Sorting
{
    /// <summary>
    /// Helper class for working with sortable DTO properties marked with [Sortable] attributes.
    /// </summary>
    public static class SortableFieldHelper
    {
        /// <summary>
        /// Gets a list of all sortable property names from a DTO type.
        /// Returns property names (e.g., "FirstName", "ActiveCoverCount") for frontend use.
        /// </summary>
        public static List<string> GetSortableFields<TDto>()
        {
            return typeof(TDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(prop => prop.GetCustomAttribute<SortableAttribute>() != null)
                .Select(prop => prop.Name)
                .ToList();
        }

        /// <summary>
        /// Gets the SQL column name for a property.
        /// Returns the custom column name if specified, otherwise auto-generates snake_case from the property name.
        /// </summary>
        /// <param name="propertyName">The property name (e.g., "FirstName", "firstName", "ActiveCoverCount").</param>
        /// <returns>The SQL column name to use in ORDER BY clauses.</returns>
        /// <exception cref="ArgumentException">Thrown when the property doesn't exist or isn't marked as sortable.</exception>
        public static string GetSqlColumnName<TDto>(string propertyName)
        {
            PropertyInfo? property = typeof(TDto).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property == null)
            {
                throw new ArgumentException($"Property '{propertyName}' not found on type {typeof(TDto).Name}");
            }

            SortableAttribute? attribute = property.GetCustomAttribute<SortableAttribute>();

            if (attribute == null)
            {
                throw new ArgumentException($"Property '{propertyName}' on type {typeof(TDto).Name} is not marked as sortable");
            }

            if (!string.IsNullOrEmpty(attribute.ColumnName))
            {
                return attribute.ColumnName;
            }

            return ToSnakeCase(property.Name);
        }

        /// <summary>
        /// Gets the default sort column name for a DTO type, if one is specified.
        /// Returns null if no property is marked as the default.
        /// </summary>
        public static string? GetDefaultSortColumn<TDto>()
        {
            PropertyInfo? defaultProperty = typeof(TDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(prop =>
                {
                    SortableAttribute? attr = prop.GetCustomAttribute<SortableAttribute>();
                    return attr != null && attr.IsDefault;
                });

            if (defaultProperty == null)
            {
                return null;
            }

            SortableAttribute attribute = defaultProperty.GetCustomAttribute<SortableAttribute>()!;

            if (!string.IsNullOrEmpty(attribute.ColumnName))
            {
                return attribute.ColumnName;
            }

            return ToSnakeCase(defaultProperty.Name);
        }

        /// <summary>
        /// Builds an ORDER BY clause from a column name and sort order.
        /// Returns an empty string if columnName is null.
        /// </summary>
        public static string BuildOrderByClause(string? columnName, SortOrder sortOrder)
        {
            if (string.IsNullOrEmpty(columnName))
            {
                return string.Empty;
            }

            string direction = sortOrder == SortOrder.Ascending ? "ASC" : "DESC";
            return $"ORDER BY {columnName} {direction}";
        }

        /// <summary>
        /// Converts a PascalCase or camelCase string to snake_case.
        /// Keeps consecutive capitals together (e.g., "ExternalID" → "external_id").
        /// </summary>
        private static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            StringBuilder result = new StringBuilder();
            bool previousWasUpper = false;
            bool previousWasLower = false;

            for (int i = 0; i < input.Length; i++)
            {
                char current = input[i];
                bool isUpper = char.IsUpper(current);
                bool isLower = char.IsLower(current);

                if (i > 0)
                {
                    if (isUpper && previousWasLower)
                    {
                        result.Append('_');
                    }
                    else if (isLower && previousWasUpper && i > 1 && char.IsUpper(input[i - 2]))
                    {
                        result.Append('_');
                    }
                }

                result.Append(char.ToLowerInvariant(current));
                previousWasUpper = isUpper;
                previousWasLower = isLower;
            }

            return result.ToString();
        }
    }
}

