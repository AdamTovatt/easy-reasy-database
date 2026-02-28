namespace EasyReasy.Database.Mapping.Benchmarks
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
