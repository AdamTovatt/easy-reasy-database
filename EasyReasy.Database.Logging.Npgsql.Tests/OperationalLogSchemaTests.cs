using EasyReasy.Database.Logging.Npgsql;

namespace EasyReasy.Database.Logging.Npgsql.Tests
{
    /// <summary>
    /// Pins <see cref="OperationalLogSchema.CreateTableSql"/>'s identifier guard. The table name is
    /// interpolated into DDL and used to derive partition/index names, so a name that isn't a bare
    /// unqualified identifier must be rejected rather than reaching the database.
    /// </summary>
    public class OperationalLogSchemaTests
    {
        [Theory]
        [InlineData("operational_log")]
        [InlineData("_audit")]
        [InlineData("Logs2024")]
        public void CreateTableSql_WithUnqualifiedIdentifier_EmitsDdlForThatTable(string tableName)
        {
            string sql = OperationalLogSchema.CreateTableSql(tableName);

            Assert.Contains($"CREATE TABLE IF NOT EXISTS {tableName} (", sql);
            Assert.Contains($"PARTITION OF {tableName}", sql);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        // Schema-qualified is rejected here even though OperationalLoggingOptions.Validate accepts it
        // for plain reads/writes: this helper derives partition and index names from the table name,
        // which only works for a bare unqualified identifier.
        [InlineData("logs.operational_log")]
        [InlineData("bad name")]
        [InlineData("log; DROP TABLE users")]
        [InlineData("1_leading_digit")]
        public void CreateTableSql_WithNonIdentifier_ThrowsArgumentException(string tableName)
        {
            Assert.Throws<ArgumentException>(() => OperationalLogSchema.CreateTableSql(tableName));
        }
    }
}
