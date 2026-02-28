namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Hybrid entity: constructor for required properties, settable properties for optional ones.
    /// Tests that constructor params and remaining settable properties both get mapped correctly.
    /// </summary>
    public class ConstructorWithExtraPropertiesEntity
    {
        public Guid Id { get; }
        public string Name { get; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public decimal? Score { get; set; }

        public ConstructorWithExtraPropertiesEntity(Guid id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
