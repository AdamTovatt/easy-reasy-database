namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Constructor-only entity (no parameterless constructor) for testing constructor-based mapping.
    /// All properties are get-only, enforcing proper NRT support.
    /// </summary>
    public class ConstructorMappingTestEntity
    {
        public Guid Id { get; }
        public string Name { get; }
        public int? Value { get; }

        public ConstructorMappingTestEntity(Guid id, string name, int? value)
        {
            Id = id;
            Name = name;
            Value = value;
        }
    }
}
