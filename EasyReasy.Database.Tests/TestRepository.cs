using EasyReasy.Database;
using System.Data.Common;

namespace EasyReasy.Database.Tests
{
    public class TestRepository : RepositoryBase
    {
        public TestRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        public async Task<int> ExecuteQueryAsync(string sql, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                DbCommand command = dbSession.Connection.CreateCommand();
                command.CommandText = sql;
                if (dbSession.Transaction != null)
                {
                    command.Transaction = dbSession.Transaction;
                }
                return await command.ExecuteNonQueryAsync();
            }, session);
        }

        public async Task<string?> ExecuteScalarAsync(string sql, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                DbCommand command = dbSession.Connection.CreateCommand();
                command.CommandText = sql;
                if (dbSession.Transaction != null)
                {
                    command.Transaction = dbSession.Transaction;
                }
                object? result = await command.ExecuteScalarAsync();
                return result?.ToString();
            }, session);
        }

        public async Task UseSessionActionAsync(Func<IDbSession, Task> action, IDbSession? session = null)
        {
            await UseSessionAsync(action, session);
        }
    }
}

