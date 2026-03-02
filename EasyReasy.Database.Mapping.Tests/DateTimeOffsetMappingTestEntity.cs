namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Entity that maps the TIMESTAMPTZ column to DateTimeOffset instead of DateTime.
    /// Used to verify that the RowDeserializer handles the DateTime → DateTimeOffset
    /// conversion needed when Npgsql returns DateTime (Kind=Utc) for timestamptz columns.
    /// </summary>
    public class DateTimeOffsetMappingTestEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>
    /// Entity with nullable DateTimeOffset for testing nullable TIMESTAMPTZ mapping.
    /// </summary>
    public class NullableDateTimeOffsetEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset? CreatedAt { get; set; }
    }
}
