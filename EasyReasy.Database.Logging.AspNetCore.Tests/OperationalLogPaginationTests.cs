using EasyReasy.Database.Logging.AspNetCore;

namespace EasyReasy.Database.Logging.AspNetCore.Tests
{
    /// <summary>
    /// Covers <see cref="OperationalLogPagination"/>, the resolver behind the operational log read
    /// endpoint's <c>page</c> / <c>perPage</c> query values: page floored at 1, perPage defaulted
    /// when absent and clamped to the maximum so an untrusted value can't request an unbounded page.
    /// </summary>
    public class OperationalLogPaginationTests
    {
        [Theory]
        [InlineData(null, 1)]
        [InlineData(0, 1)]
        [InlineData(-5, 1)]
        [InlineData(1, 1)]
        [InlineData(7, 7)]
        public void ResolvePage_FloorsAtOne(int? input, int expected)
        {
            Assert.Equal(expected, OperationalLogPagination.ResolvePage(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        [InlineData(-5)]
        public void ResolvePerPage_WhenMissingOrNonPositive_UsesDefault(int? input)
        {
            Assert.Equal(OperationalLogPagination.DefaultPerPage, OperationalLogPagination.ResolvePerPage(input));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(25, 25)]
        public void ResolvePerPage_WithinBounds_IsUnchanged(int input, int expected)
        {
            Assert.Equal(expected, OperationalLogPagination.ResolvePerPage(input));
        }

        [Theory]
        [InlineData(101)]
        [InlineData(1000)]
        [InlineData(int.MaxValue)]
        public void ResolvePerPage_AboveMax_IsClampedToMax(int input)
        {
            Assert.Equal(OperationalLogPagination.MaxPerPage, OperationalLogPagination.ResolvePerPage(input));
        }

        [Fact]
        public void ResolvePerPage_AtMax_IsUnchanged()
        {
            Assert.Equal(OperationalLogPagination.MaxPerPage, OperationalLogPagination.ResolvePerPage(OperationalLogPagination.MaxPerPage));
        }
    }
}
