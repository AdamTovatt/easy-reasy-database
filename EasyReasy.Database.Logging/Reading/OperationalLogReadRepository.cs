using System.Data.Common;
using EasyReasy.Database.Logging.Models;
using EasyReasy.Database.Mapping;
using EasyReasy.Database.Pagination;

namespace EasyReasy.Database.Logging.Reading
{
    /// <summary>
    /// Default <see cref="IOperationalLogReadRepository"/>: a provider-agnostic read over the
    /// operational log table via <see cref="EasyReasy.Database.Mapping"/>. Each call opens one
    /// connection from the registered <see cref="DbDataSource"/> and issues a count + page in a
    /// single round trip.
    /// </summary>
    public sealed class OperationalLogReadRepository : IOperationalLogReadRepository
    {
        private readonly DbDataSource _dataSource;
        private readonly OperationalLoggingOptions _options;

        /// <summary>Creates the repository over the given data source and options.</summary>
        public OperationalLogReadRepository(DbDataSource dataSource, OperationalLoggingOptions options)
        {
            _dataSource = dataSource;
            _options = options;
        }

        /// <inheritdoc/>
        public async Task<PagedResult<OperationalLogEntry>> GetAsync(OperationalLogFilters filters, int page, int perPage)
        {
            long offset = (long)(page - 1) * perPage;

            List<string> conditions = new List<string>();

            if (!string.IsNullOrWhiteSpace(filters.Level))
            {
                conditions.Add("level = @level");
            }

            if (!string.IsNullOrWhiteSpace(filters.SourceContext))
            {
                conditions.Add("source_context = @sourceContext");
            }

            if (filters.From.HasValue)
            {
                conditions.Add("created_at >= @from");
            }

            if (filters.To.HasValue)
            {
                conditions.Add("created_at < @to");
            }

            string whereClause = conditions.Count > 0
                ? "WHERE " + string.Join(" AND ", conditions)
                : string.Empty;

            string sql = $@"
                SELECT COUNT(*)
                FROM {_options.TableName}
                {whereClause};

                SELECT id, created_at, level, source_context, message, message_template,
                       exception, correlation_id, user_id, properties
                FROM {_options.TableName}
                {whereClause}
                ORDER BY created_at DESC, id DESC
                LIMIT @perPage OFFSET @offset";

            object parameters = new
            {
                level = filters.Level,
                sourceContext = filters.SourceContext,
                from = filters.From,
                to = filters.To,
                perPage,
                offset,
            };

            await using DbConnection connection = await _dataSource.OpenConnectionAsync();
            await using GridReader gridReader = await connection.QueryMultipleAsync(sql, parameters);

            long totalCount = await gridReader.ReadSingleAsync<long>();
            List<OperationalLogEntry> items = (await gridReader.ReadAsync<OperationalLogEntry>()).ToList();

            return new PagedResult<OperationalLogEntry>(totalCount, items, page, perPage);
        }
    }
}
