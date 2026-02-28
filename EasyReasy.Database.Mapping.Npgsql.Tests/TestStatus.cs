using EasyReasy.Database.Mapping;

namespace EasyReasy.Database.Mapping.Npgsql.Tests
{
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
