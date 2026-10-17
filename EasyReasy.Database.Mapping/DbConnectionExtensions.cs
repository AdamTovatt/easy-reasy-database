using System.Data.Common;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Extension methods on DbConnection for executing SQL queries and commands.
    /// These mirror the Dapper API signatures used in the codebase, making migration
    /// a simple using-statement swap.
    /// </summary>
    public static class DbConnectionExtensions
    {
        /// <summary>
        /// Executes a query and returns the resulting rows deserialized into <typeparamref name="T"/>,
        /// or column 0 of each row when <typeparamref name="T"/> is a scalar type.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize each row into, or the scalar type to read.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>An enumerable of deserialized entities or scalar values.</returns>
        public static async Task<IEnumerable<T>> QueryAsync<T>(
            this DbConnection connection,
            string sql,
            object? param = null,
            DbTransaction? transaction = null)
        {
            await EnsureOpenAsync(connection);

            await using (DbCommand command = CreateCommand(connection, sql, param, transaction))
            await using (DbDataReader reader = await command.ExecuteReaderAsync())
            {
                return await RowDeserializer.ReadAllAsync<T>(reader);
            }
        }

        /// <summary>
        /// Executes a query and returns exactly one row deserialized into <typeparamref name="T"/>,
        /// or its column 0 when <typeparamref name="T"/> is a scalar type.
        /// Throws if the query returns zero or more than one row. For a scalar type, a NULL value in the
        /// one row is returned as null rather than treated as a missing row.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize, or the scalar type to read.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>The single deserialized entity or scalar value.</returns>
        public static async Task<T> QuerySingleAsync<T>(
            this DbConnection connection,
            string sql,
            object? param = null,
            DbTransaction? transaction = null)
        {
            await EnsureOpenAsync(connection);

            await using (DbCommand command = CreateCommand(connection, sql, param, transaction))
            await using (DbDataReader reader = await command.ExecuteReaderAsync())
            {
                return await RowDeserializer.ReadSingleAsync<T>(reader);
            }
        }

        /// <summary>
        /// Executes a query and returns zero or one row deserialized into <typeparamref name="T"/>,
        /// or its column 0 when <typeparamref name="T"/> is a scalar type.
        /// Returns the default value if no rows are returned. Throws if more than one row is returned.
        /// For a nullable scalar type, no rows and one row holding NULL both return null.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize, or the scalar type to read.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>The deserialized entity or scalar value, or default if no rows.</returns>
        public static async Task<T?> QuerySingleOrDefaultAsync<T>(
            this DbConnection connection,
            string sql,
            object? param = null,
            DbTransaction? transaction = null)
        {
            await EnsureOpenAsync(connection);

            await using (DbCommand command = CreateCommand(connection, sql, param, transaction))
            await using (DbDataReader reader = await command.ExecuteReaderAsync())
            {
                return await RowDeserializer.ReadSingleOrDefaultAsync<T>(reader);
            }
        }

        /// <summary>
        /// Executes a query and returns the first row deserialized into <typeparamref name="T"/>
        /// (or its column 0 when <typeparamref name="T"/> is a scalar type),
        /// or the default value if no rows are returned. Does not throw if multiple rows are returned.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize, or the scalar type to read.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>The first deserialized entity or scalar value, or default if no rows.</returns>
        public static async Task<T?> QueryFirstOrDefaultAsync<T>(
            this DbConnection connection,
            string sql,
            object? param = null,
            DbTransaction? transaction = null)
        {
            await EnsureOpenAsync(connection);

            await using (DbCommand command = CreateCommand(connection, sql, param, transaction))
            await using (DbDataReader reader = await command.ExecuteReaderAsync())
            {
                return await RowDeserializer.ReadFirstOrDefaultAsync<T>(reader);
            }
        }

        /// <summary>
        /// Executes a non-query command (INSERT, UPDATE, DELETE) and returns the number of rows affected.
        /// </summary>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL command to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as command parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>The number of rows affected.</returns>
        public static async Task<int> ExecuteAsync(
            this DbConnection connection,
            string sql,
            object? param = null,
            DbTransaction? transaction = null)
        {
            await EnsureOpenAsync(connection);

            await using (DbCommand command = CreateCommand(connection, sql, param, transaction))
            {
                return await command.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// Executes a query and returns the first column of the first row as <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The scalar type to return.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>The scalar value, or default if null.</returns>
        public static async Task<T?> ExecuteScalarAsync<T>(
            this DbConnection connection,
            string sql,
            object? param = null,
            DbTransaction? transaction = null)
        {
            await EnsureOpenAsync(connection);

            await using (DbCommand command = CreateCommand(connection, sql, param, transaction))
            {
                object? result = await command.ExecuteScalarAsync();

                if (result == null || result is DBNull)
                {
                    return default;
                }

                return RowDeserializer.ConvertValue<T>(result, typeof(T));
            }
        }

        /// <summary>
        /// Executes a query that returns multiple result sets, accessible via the returned GridReader.
        /// The caller must dispose the GridReader when done.
        /// </summary>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute (may contain multiple statements separated by semicolons).</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>A GridReader for reading sequential result sets.</returns>
        public static async Task<GridReader> QueryMultipleAsync(
            this DbConnection connection,
            string sql,
            object? param = null,
            DbTransaction? transaction = null)
        {
            await EnsureOpenAsync(connection);

            DbCommand command = CreateCommand(connection, sql, param, transaction);
            DbDataReader reader = await command.ExecuteReaderAsync();

            return new GridReader(reader, command);
        }

        private static DbCommand CreateCommand(DbConnection connection, string sql, object? param, DbTransaction? transaction)
        {
            DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;

            ParameterBinder.BindParameters(command, param);

            return command;
        }

        private static async Task EnsureOpenAsync(DbConnection connection)
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }
        }

    }
}
