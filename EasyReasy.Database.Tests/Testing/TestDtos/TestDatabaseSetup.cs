using EasyReasy.Database.Testing;
using System.Data.Common;

namespace EasyReasy.Database.Tests.Testing.TestDtos
{
    public class TestDatabaseSetup : ITestDatabaseSetup
    {
        public bool ResetDatabaseWasCalled { get; private set; }
        public bool SetupDatabaseWasCalled { get; private set; }
        public DbConnection? ResetConnection { get; private set; }

        public async Task ResetDatabaseAsync(DbConnection connection)
        {
            ResetDatabaseWasCalled = true;
            ResetConnection = connection;

            DbCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS test_setup (id INTEGER PRIMARY KEY)";
            await command.ExecuteNonQueryAsync();
        }

        public void SetupDatabase()
        {
            SetupDatabaseWasCalled = true;
        }

        public void Reset()
        {
            ResetDatabaseWasCalled = false;
            SetupDatabaseWasCalled = false;
            ResetConnection = null;
        }
    }
}

