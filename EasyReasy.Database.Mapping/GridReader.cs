using System.Data.Common;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Reads multiple result sets from a single database command, similar to Dapper's GridReader.
    /// Each call to ReadAsync or ReadSingleAsync advances to the next result set.
    /// </summary>
    public class GridReader : IAsyncDisposable
    {
        private readonly DbDataReader _reader;
        private readonly DbCommand _command;
        private bool _isFirstRead = true;

        internal GridReader(DbDataReader reader, DbCommand command)
        {
            _reader = reader;
            _command = command;
        }

        /// <summary>
        /// Reads all rows from the current result set and advances to the next one.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize each row into.</typeparam>
        /// <returns>An enumerable of deserialized entities.</returns>
        public async Task<IEnumerable<T>> ReadAsync<T>()
        {
            await AdvanceToNextResultIfNeededAsync();

            List<T> results = await RowDeserializer.DeserializeAsync<T>(_reader);

            return results;
        }

        /// <summary>
        /// Reads a single row (or scalar) from the current result set and advances to the next one.
        /// Throws if the result set contains zero or more than one row.
        /// </summary>
        /// <typeparam name="T">The type to read.</typeparam>
        /// <returns>The single value.</returns>
        public async Task<T> ReadSingleAsync<T>()
        {
            await AdvanceToNextResultIfNeededAsync();

            Type targetType = typeof(T);
            bool isSimpleType = IsSimpleType(targetType);

            if (isSimpleType)
            {
                T? result = await RowDeserializer.ReadScalarAsync<T>(_reader);

                // Verify no additional rows
                if (await _reader.ReadAsync())
                {
                    throw new InvalidOperationException("Sequence contains more than one element.");
                }

                if (result == null)
                {
                    throw new InvalidOperationException("Sequence contains no elements.");
                }

                return result;
            }
            else
            {
                List<T> results = await RowDeserializer.DeserializeAsync<T>(_reader);

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

        private async Task AdvanceToNextResultIfNeededAsync()
        {
            if (_isFirstRead)
            {
                _isFirstRead = false;
                return;
            }

            await _reader.NextResultAsync();
        }

        private static bool IsSimpleType(Type type)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType.IsPrimitive
                || underlyingType == typeof(string)
                || underlyingType == typeof(decimal)
                || underlyingType == typeof(DateTime)
                || underlyingType == typeof(DateTimeOffset)
                || underlyingType == typeof(Guid)
                || underlyingType.IsEnum;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _reader.DisposeAsync();
            await _command.DisposeAsync();
        }
    }
}
