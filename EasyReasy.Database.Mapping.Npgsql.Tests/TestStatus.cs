using EasyReasy.Database.Mapping;

namespace EasyReasy.Database.Mapping.Npgsql.Tests
{
    [DbEnum("npgsql_mapping_test_status")]
    public enum TestStatus
    {
        [DbName("active")]
        Active,

        [DbName("inactive")]
        Inactive,

        [DbName("pending")]
        Pending
    }
}
