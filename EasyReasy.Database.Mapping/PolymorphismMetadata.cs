namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Cached discriminator metadata read from <c>[JsonPolymorphic]</c> and
    /// <c>[JsonDerivedType]</c> attributes via reflection.
    /// </summary>
    internal sealed class PolymorphismMetadata
    {
        public string DiscriminatorPropertyName { get; }
        public IReadOnlyList<(object Discriminator, Type DerivedType)> DerivedTypes { get; }

        public PolymorphismMetadata(
            string discriminatorPropertyName,
            IReadOnlyList<(object Discriminator, Type DerivedType)> derivedTypes)
        {
            DiscriminatorPropertyName = discriminatorPropertyName;
            DerivedTypes = derivedTypes;
        }
    }
}
