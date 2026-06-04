using EasyReasy.Database.Logging.Models;
using EasyReasy.Database.Pagination;

namespace EasyReasy.Database.Logging.Reading
{
    /// <summary>
    /// Paginated read surface over the operational log table. Intended for an admin log viewer.
    /// Reads are ordered most-recent-first.
    /// </summary>
    public interface IOperationalLogReadRepository
    {
        /// <summary>
        /// Returns one page of operational log rows matching <paramref name="filters"/>, together
        /// with the total matching count.
        /// </summary>
        /// <param name="filters">Optional column filters; null properties impose no constraint.</param>
        /// <param name="page">The 1-based page number.</param>
        /// <param name="perPage">The page size.</param>
        Task<PagedResult<OperationalLogEntry>> GetAsync(OperationalLogFilters filters, int page, int perPage);
    }
}
