namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Specifies the database name for an enum field, used by <see cref="DbNameEnumHandler{T}"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class DbNameAttribute : Attribute
    {
        public string Name { get; }

        public DbNameAttribute(string name) => Name = name;
    }
}
