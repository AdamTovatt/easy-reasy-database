namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// The connection the database-backed tests in this project run against. A dedicated variable rather
    /// than a generic <c>DATABASE_CONNECTION_STRING</c>, so an ambient value meant for another project on
    /// the same machine cannot leak in.
    /// </summary>
    internal static class TestConnection
    {
        internal static readonly string ConnectionString =
            Environment.GetEnvironmentVariable("EASYREASY_TESTING_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    }
}
