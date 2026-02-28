namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Test enum mapped to PostgreSQL mapping_test_status type.
    /// </summary>
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
