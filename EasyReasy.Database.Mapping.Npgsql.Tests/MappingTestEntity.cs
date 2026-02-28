namespace EasyReasy.Database.Mapping.Npgsql.Tests
{
    public class MappingTestEntity
    {
        public string Name { get; set; } = string.Empty;
        public TestStatus? Status { get; set; }
    }
}
