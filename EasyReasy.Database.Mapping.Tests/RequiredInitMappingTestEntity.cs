namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Entity with `required init` properties — the modern immutable-domain-model
    /// shape. Used to pin whether the mapper can hydrate types of this shape
    /// directly, or whether consumers are forced into an intermediate Row class.
    /// </summary>
    public class RequiredInitMappingTestEntity
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }
        public int? Value { get; init; }
    }
}
