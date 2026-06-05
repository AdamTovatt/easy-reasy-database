namespace EasyReasy.Database.Logging.AspNetCore
{
    /// <summary>
    /// Resolves the optional <c>page</c> / <c>perPage</c> query values for the operational log read
    /// endpoint into safe bounds: page is floored at 1, and perPage defaults to
    /// <see cref="DefaultPerPage"/> and is clamped to <see cref="MaxPerPage"/> so an untrusted value
    /// can't pull an unbounded result set into memory. The response's <c>TotalCount</c> still reports
    /// the full match count, so a clamped client can tell there is more.
    /// </summary>
    internal static class OperationalLogPagination
    {
        internal const int DefaultPerPage = 50;
        internal const int MaxPerPage = 100;

        internal static int ResolvePage(int? page) => page is > 0 ? page.Value : 1;

        internal static int ResolvePerPage(int? perPage) =>
            perPage is > 0 ? Math.Min(perPage.Value, MaxPerPage) : DefaultPerPage;
    }
}
