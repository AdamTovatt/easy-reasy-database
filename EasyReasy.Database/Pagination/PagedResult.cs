namespace EasyReasy.Database.Pagination
{
    /// <summary>
    /// Represents a single page of a larger result set, together with the total number of items
    /// across all pages and the pagination metadata describing which page this is.
    /// </summary>
    /// <typeparam name="T">The type of items in the result.</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// Gets the total count of items across all pages.
        /// </summary>
        public long TotalCount { get; }

        /// <summary>
        /// Gets the items belonging to the current page.
        /// </summary>
        public IReadOnlyList<T> Items { get; }

        /// <summary>
        /// Gets the current page number (1-based).
        /// </summary>
        public int Page { get; }

        /// <summary>
        /// Gets the number of items per page.
        /// </summary>
        public int PerPage { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PagedResult{T}"/> class.
        /// </summary>
        /// <param name="totalCount">The total count of items across all pages.</param>
        /// <param name="items">The items belonging to the current page.</param>
        /// <param name="page">The current page number (1-based).</param>
        /// <param name="perPage">The number of items per page.</param>
        public PagedResult(long totalCount, IReadOnlyList<T> items, int page, int perPage)
        {
            TotalCount = totalCount;
            Items = items;
            Page = page;
            PerPage = perPage;
        }
    }
}
