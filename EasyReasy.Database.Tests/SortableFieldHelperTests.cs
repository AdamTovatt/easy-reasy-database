using EasyReasy.Database.Sorting;
using EasyReasy.Database.Tests.TestDtos;

namespace EasyReasy.Database.Tests
{
    public class SortableFieldHelperTests
    {
        [Fact]
        public void GetSortableFields_WhenCalled_ReturnsAllSortablePropertyNames()
        {
            List<string> fields = SortableFieldHelper.GetSortableFields<TestDto>();

            Assert.Contains("FirstName", fields);
            Assert.Contains("LastName", fields);
            Assert.Contains("Age", fields);
            Assert.Contains("ExternalId", fields);
            Assert.Contains("ActiveCoverCount", fields);
            Assert.DoesNotContain("NotSortable", fields);
        }

        [Fact]
        public void GetSqlColumnName_WhenCustomColumnNameSpecified_ReturnsCustomName()
        {
            string columnName = SortableFieldHelper.GetSqlColumnName<TestDto>("LastName");

            Assert.Equal("last_name", columnName);
        }

        [Fact]
        public void GetSqlColumnName_WhenNoCustomColumnName_ReturnsSnakeCase()
        {
            string columnName = SortableFieldHelper.GetSqlColumnName<TestDto>("FirstName");

            Assert.Equal("first_name", columnName);
        }

        [Fact]
        public void GetSqlColumnName_WhenPropertyNameIsCaseInsensitive_ReturnsCorrectColumnName()
        {
            string columnName1 = SortableFieldHelper.GetSqlColumnName<TestDto>("FirstName");
            string columnName2 = SortableFieldHelper.GetSqlColumnName<TestDto>("firstname");
            string columnName3 = SortableFieldHelper.GetSqlColumnName<TestDto>("FIRSTNAME");

            Assert.Equal("first_name", columnName1);
            Assert.Equal("first_name", columnName2);
            Assert.Equal("first_name", columnName3);
        }

        [Fact]
        public void GetSqlColumnName_WhenPropertyDoesNotExist_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => SortableFieldHelper.GetSqlColumnName<TestDto>("NonExistent"));
        }

        [Fact]
        public void GetSqlColumnName_WhenPropertyNotSortable_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => SortableFieldHelper.GetSqlColumnName<TestDto>("NotSortable"));
        }

        [Fact]
        public void GetSqlColumnName_WhenConsecutiveCapitals_HandlesCorrectly()
        {
            string columnName = SortableFieldHelper.GetSqlColumnName<TestDto>("ActiveCoverCount");

            Assert.Equal("active_cover_count", columnName);
        }

        [Fact]
        public void GetDefaultSortColumn_WhenDefaultExists_ReturnsDefaultColumnName()
        {
            string? defaultColumn = SortableFieldHelper.GetDefaultSortColumn<TestDto>();

            Assert.Equal("age", defaultColumn);
        }

        [Fact]
        public void GetDefaultSortColumn_WhenDefaultHasCustomColumnName_ReturnsCustomName()
        {
            string? defaultColumn = SortableFieldHelper.GetDefaultSortColumn<CustomDto>();

            Assert.Equal("custom_age", defaultColumn);
        }

        [Fact]
        public void GetDefaultSortColumn_WhenNoDefault_ReturnsNull()
        {
            string? defaultColumn = SortableFieldHelper.GetDefaultSortColumn<NoDefaultDto>();

            Assert.Null(defaultColumn);
        }

        [Fact]
        public void BuildOrderByClause_WhenColumnNameProvided_ReturnsCorrectClause()
        {
            string clause = SortableFieldHelper.BuildOrderByClause("first_name", SortOrder.Ascending);

            Assert.Equal("ORDER BY first_name ASC", clause);
        }

        [Fact]
        public void BuildOrderByClause_WhenDescending_ReturnsDescClause()
        {
            string clause = SortableFieldHelper.BuildOrderByClause("first_name", SortOrder.Descending);

            Assert.Equal("ORDER BY first_name DESC", clause);
        }

        [Fact]
        public void BuildOrderByClause_WhenColumnNameIsNull_ReturnsEmptyString()
        {
            string clause = SortableFieldHelper.BuildOrderByClause(null, SortOrder.Ascending);

            Assert.Equal(string.Empty, clause);
        }

        [Fact]
        public void BuildOrderByClause_WhenColumnNameIsEmpty_ReturnsEmptyString()
        {
            string clause = SortableFieldHelper.BuildOrderByClause(string.Empty, SortOrder.Ascending);

            Assert.Equal(string.Empty, clause);
        }
    }
}

