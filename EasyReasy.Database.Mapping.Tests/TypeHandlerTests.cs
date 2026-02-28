using System.Data;
using System.Data.Common;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Tests for the type handler system, including the core fix for enum type handlers
    /// that Dapper ignores due to its hardcoded enum-to-integer fast path.
    /// </summary>
    [Collection("Database")]
    public class TypeHandlerTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public TypeHandlerTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            _connection = await _fixture.DataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();
        }

        public async Task DisposeAsync()
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
            TypeHandlerRegistry.Clear();
        }

        #region Enum Type Handler — The Core Fix

        [Fact]
        public async Task EnumHandler_WriteAndRead_RoundTrips()
        {
            TypeHandlerRegistry.AddTypeHandler(new DbNameEnumHandler<TestStatus>());

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, status) VALUES (@name, @status::mapping_test_status)",
                new { name = "enum_handler_test", status = TestStatus.Active },
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM mapping_test WHERE name = @name",
                new { name = "enum_handler_test" },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal(TestStatus.Active, result.Status);
        }

        [Fact]
        public async Task EnumHandler_WriteAllValues_CorrectlyStored()
        {
            TypeHandlerRegistry.AddTypeHandler(new DbNameEnumHandler<TestStatus>());

            TestStatus[] allStatuses = new[] { TestStatus.Active, TestStatus.Inactive, TestStatus.Pending };

            foreach (TestStatus status in allStatuses)
            {
                string name = $"enum_all_{status}";
                await _connection.ExecuteAsync(
                    "INSERT INTO mapping_test (name, status) VALUES (@name, @status::mapping_test_status)",
                    new { name, status },
                    _transaction);
            }

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM mapping_test WHERE name LIKE 'enum_all_%' ORDER BY name",
                transaction: _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Equal(3, list.Count);
            Assert.Equal(TestStatus.Active, list[0].Status);
            Assert.Equal(TestStatus.Inactive, list[1].Status);
            Assert.Equal(TestStatus.Pending, list[2].Status);
        }

        [Fact]
        public async Task EnumHandler_NullEnumValue_StoredAsNull()
        {
            TypeHandlerRegistry.AddTypeHandler(new DbNameEnumHandler<TestStatus>());

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, status) VALUES (@name, @status)",
                new { name = "enum_null_test", status = (TestStatus?)null },
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM mapping_test WHERE name = @name",
                new { name = "enum_null_test" },
                _transaction);

            Assert.NotNull(result);
            Assert.Null(result.Status);
        }

        [Fact]
        public async Task EnumHandler_FilterByEnumParameter_Works()
        {
            TypeHandlerRegistry.AddTypeHandler(new DbNameEnumHandler<TestStatus>());

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, status) VALUES (@name, @status::mapping_test_status)",
                new { name = "enum_filter_a", status = TestStatus.Active },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, status) VALUES (@name, @status::mapping_test_status)",
                new { name = "enum_filter_b", status = TestStatus.Inactive },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM mapping_test WHERE status = @status::mapping_test_status AND name LIKE 'enum_filter_%'",
                new { status = TestStatus.Active },
                _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Single(list);
            Assert.Equal("enum_filter_a", list[0].Name);
        }

        #endregion

        #region Custom Type Handler

        [Fact]
        public async Task CustomHandler_TransformsValueOnWrite()
        {
            TypeHandlerRegistry.AddTypeHandler(new PrefixedStringHandler("PREFIX_"));

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, description) VALUES ('custom_write', @description)",
                new { description = "hello" },
                _transaction);

            // Read raw value without handler to verify the transformation
            TypeHandlerRegistry.Clear();

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT description AS Description FROM mapping_test WHERE name = 'custom_write'",
                transaction: _transaction);

            Assert.NotNull(result);
            Assert.Equal("PREFIX_hello", result.Description);
        }

        [Fact]
        public async Task CustomHandler_TransformsValueOnRead()
        {
            // Insert raw value
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, description) VALUES ('custom_read', 'PREFIX_world')",
                transaction: _transaction);

            // Register handler that strips prefix on read
            TypeHandlerRegistry.AddTypeHandler(new PrefixedStringHandler("PREFIX_"));

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT description AS Description FROM mapping_test WHERE name = 'custom_read'",
                transaction: _transaction);

            Assert.NotNull(result);
            Assert.Equal("world", result.Description);
        }

        #endregion

        #region Handler Registration

        [Fact]
        public async Task AddTypeHandler_Generic_RegistersForType()
        {
            DbNameEnumHandler<TestStatus> handler = new DbNameEnumHandler<TestStatus>();
            TypeHandlerRegistry.AddTypeHandler(handler);

            Assert.True(TypeHandlerRegistry.TryGetHandler(typeof(TestStatus), out ITypeHandler? retrieved));
            Assert.Same(handler, retrieved);
        }

        [Fact]
        public async Task AddTypeHandler_ByType_RegistersForType()
        {
            DbNameEnumHandler<TestStatus> handler = new DbNameEnumHandler<TestStatus>();
            TypeHandlerRegistry.AddTypeHandler(typeof(TestStatus), handler);

            Assert.True(TypeHandlerRegistry.TryGetHandler(typeof(TestStatus), out ITypeHandler? retrieved));
            Assert.Same(handler, retrieved);
        }

        [Fact]
        public async Task TryGetHandler_UnregisteredType_ReturnsFalse()
        {
            Assert.False(TypeHandlerRegistry.TryGetHandler(typeof(int), out _));
        }

        #endregion

        #region DbNameEnumHandler Error Cases

        [Fact]
        public void DbNameEnumHandler_EnumWithoutDbName_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => new DbNameEnumHandler<DayOfWeek>());
        }

        #endregion

        #region Test Type Handlers

        /// <summary>
        /// Test handler that adds/strips a prefix for string values.
        /// </summary>
        private class PrefixedStringHandler : TypeHandler<string>
        {
            private readonly string _prefix;

            public PrefixedStringHandler(string prefix)
            {
                _prefix = prefix;
            }

            public override void SetValue(IDbDataParameter parameter, string value)
            {
                parameter.Value = _prefix + value;
                parameter.DbType = DbType.String;
            }

            public override string? Parse(object value)
            {
                string stringValue = value.ToString()!;
                if (stringValue.StartsWith(_prefix))
                {
                    return stringValue.Substring(_prefix.Length);
                }
                return stringValue;
            }
        }

        #endregion
    }
}
