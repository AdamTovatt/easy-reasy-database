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
        /// Reads all rows (or column 0 of each row, for a scalar type) from the current result set
        /// and advances to the next one.
        /// </summary>
        /// <typeparam name="T">The entity type to deserialize each row into, or the scalar type to read.</typeparam>
        /// <returns>An enumerable of deserialized entities or scalar values.</returns>
        public async Task<IEnumerable<T>> ReadAsync<T>()
        {
            await AdvanceToNextResultIfNeededAsync();

            return await RowDeserializer.ReadAllAsync<T>(_reader);
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

            return await RowDeserializer.ReadSingleAsync<T>(_reader);
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

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _reader.DisposeAsync();
            await _command.DisposeAsync();
        }
    }
}
