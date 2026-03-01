namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Specifies the database enum type name for an enum type.
    /// Used by provider-specific extensions (e.g. <c>MapDbNameEnum</c>) to register
    /// both the database type mapping and the <see cref="DbNameEnumHandler{T}"/> in one step.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum)]
    public class DbEnumAttribute : Attribute
    {
        /// <summary>
        /// The database enum type name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Specifies the database enum type name for this enum.
        /// </summary>
        /// <param name="name">The database enum type name (e.g. <c>"my_status"</c>).</param>
        public DbEnumAttribute(string name) => Name = name;
    }
}
