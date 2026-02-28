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
        /// Executes a query and returns the resulting rows deserialized into <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize each row into.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>An enumerable of deserialized entities.</returns>
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
                return await RowDeserializer.DeserializeAsync<T>(reader);
            }
        }

        /// <summary>
        /// Executes a query and returns exactly one row deserialized into <typeparamref name="T"/>.
        /// Throws if the query returns zero or more than one row.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>The single deserialized entity.</returns>
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
                Type targetType = typeof(T);

                if (IsSimpleType(targetType))
                {
                    T? result = await RowDeserializer.ReadScalarAsync<T>(reader);

                    if (await reader.ReadAsync())
                    {
                        throw new InvalidOperationException("Sequence contains more than one element.");
                    }

                    if (result == null)
                    {
                        throw new InvalidOperationException("Sequence contains no elements.");
                    }

                    return result;
                }

                List<T> results = await RowDeserializer.DeserializeAsync<T>(reader);

                if (results.Count == 0)
                {
                    throw new InvalidOperationException("Sequence contains no elements.");
                }

                if (results.Count > 1)
                {
                    throw new InvalidOperationException("Sequence contains more than one element.");
                }

                return results[0];
            }
        }

        /// <summary>
        /// Executes a query and returns zero or one row deserialized into <typeparamref name="T"/>.
        /// Returns the default value if no rows are returned. Throws if more than one row is returned.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>The deserialized entity, or default if no rows.</returns>
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
                Type targetType = typeof(T);

                if (IsSimpleType(targetType))
                {
                    T? result = await RowDeserializer.ReadScalarAsync<T>(reader);

                    if (await reader.ReadAsync())
                    {
                        throw new InvalidOperationException("Sequence contains more than one element.");
                    }

                    return result;
                }

                List<T> results = await RowDeserializer.DeserializeAsync<T>(reader);

                if (results.Count > 1)
                {
                    throw new InvalidOperationException("Sequence contains more than one element.");
                }

                return results.Count == 0 ? default : results[0];
            }
        }

        /// <summary>
        /// Executes a query and returns the first row deserialized into <typeparamref name="T"/>,
        /// or the default value if no rows are returned. Does not throw if multiple rows are returned.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize.</typeparam>
        /// <param name="connection">The database connection.</param>
        /// <param name="sql">The SQL query to execute.</param>
        /// <param name="param">An anonymous object whose properties are used as query parameters.</param>
        /// <param name="transaction">An optional transaction to associate with the command.</param>
        /// <returns>The first deserialized entity, or default if no rows.</returns>
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
                Type targetType = typeof(T);

                if (IsSimpleType(targetType))
                {
                    return await RowDeserializer.ReadScalarAsync<T>(reader);
                }

                List<T> results = await RowDeserializer.DeserializeAsync<T>(reader);

                return results.Count == 0 ? default : results[0];
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

                Type targetType = typeof(T);
                Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                // Check handler first
                if (TypeHandlerRegistry.TryGetHandler(underlyingType, out ITypeHandler? handler) && handler != null)
                {
                    return (T?)handler.Parse(underlyingType, result);
                }

                if (result is T typedResult)
                {
                    return typedResult;
                }

                return (T)Convert.ChangeType(result, underlyingType);
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

        private static bool IsSimpleType(Type type)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType.IsPrimitive
                || underlyingType == typeof(string)
                || underlyingType == typeof(decimal)
                || underlyingType == typeof(DateTime)
                || underlyingType == typeof(DateTimeOffset)
                || underlyingType == typeof(DateOnly)
                || underlyingType == typeof(TimeOnly)
                || underlyingType == typeof(Guid)
                || underlyingType.IsEnum;
        }
    }
}
