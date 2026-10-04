using System.Data.Common;

namespace EasyReasy.Database.Tests.TestDoubles
{
    /// <summary>
    /// Data source that hands out one given connection, so a test can inspect it afterwards.
    /// </summary>
    internal sealed class SingleConnectionDataSource : DbDataSource
    {
        private readonly DbConnection _connection;

        public SingleConnectionDataSource(DbConnection connection)
        {
            _connection = connection;
        }

        public override string ConnectionString => string.Empty;

        protected override DbConnection CreateDbConnection()
        {
            return _connection;
        }
    }
}
