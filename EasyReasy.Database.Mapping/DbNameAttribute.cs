namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Specifies the database name for an enum field, used by <see cref="DbNameEnumHandler{T}"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class DbNameAttribute : Attribute
    {
        /// <summary>
        /// The database name for this enum field.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Specifies the database name for an enum field.
        /// </summary>
        /// <param name="name">The string representation used in the database.</param>
        public DbNameAttribute(string name) => Name = name;
    }
}
