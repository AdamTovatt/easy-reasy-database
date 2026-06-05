using EasyReasy.Database.Pagination;

namespace EasyReasy.Database.Tests
{
    public class PagedResultTests
    {
        [Fact]
        public void Constructor_WhenGivenPageData_ExposesAllProperties()
        {
            List<string> items = new List<string> { "a", "b" };

            PagedResult<string> result = new PagedResult<string>(totalCount: 42, items: items, page: 2, perPage: 10);

            Assert.Equal(42, result.TotalCount);
            Assert.Equal(items, result.Items);
            Assert.Equal(2, result.Page);
            Assert.Equal(10, result.PerPage);
        }

        [Fact]
        public void Constructor_WhenGivenEmptyPage_ExposesEmptyItemsAndZeroTotal()
        {
            PagedResult<int> result = new PagedResult<int>(totalCount: 0, items: new List<int>(), page: 1, perPage: 25);

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        }
    }
}
