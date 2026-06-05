using EasyReasy.Database.Logging;

namespace EasyReasy.Database.Logging.Tests
{
    /// <summary>
    /// Pins the <see cref="OperationalLoggingOptions.Validate"/> guard — the table name is
    /// interpolated into SQL, so the validation boundary that rejects non-identifier names is a
    /// SQL-injection guard and must reject anything that isn't a bare (optionally schema-qualified)
    /// identifier.
    /// </summary>
    public class OperationalLoggingOptionsTests
    {
        [Theory]
        [InlineData("operational_log")]
        // Schema-qualified is accepted here (the read/write SQL can target schema.table). Note the
        // partitioned DDL helper OperationalLogSchema.CreateTableSql deliberately rejects this form,
        // since it needs a bare name to derive partition names — see OperationalLogSchemaTests.
        [InlineData("logs.operational_log")]
        [InlineData("_private")]
        public void Validate_WithValidIdentifier_DoesNotThrow(string tableName)
        {
            OperationalLoggingOptions options = new OperationalLoggingOptions { TableName = tableName };

            options.Validate();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("bad name")]
        [InlineData("logs; DROP TABLE users")]
        [InlineData("1_starts_with_digit")]
        [InlineData("schema.table.extra")]
        public void Validate_WithInvalidIdentifier_ThrowsArgumentException(string tableName)
        {
            OperationalLoggingOptions options = new OperationalLoggingOptions { TableName = tableName };

            Assert.Throws<ArgumentException>(() => options.Validate());
        }
    }
}
