using EasyReasy.Database.Sorting;

namespace EasyReasy.Database.Tests.TestDtos
{
    public class TestDto
    {
        [Sortable]
        public string FirstName { get; set; } = string.Empty;

        [Sortable(columnName: "last_name")]
        public string LastName { get; set; } = string.Empty;

        [Sortable(isDefault: true)]
        public int Age { get; set; }

        [Sortable(columnName: "external_id", isDefault: false)]
        public string ExternalId { get; set; } = string.Empty;

        public string NotSortable { get; set; } = string.Empty;

        [Sortable]
        public string ActiveCoverCount { get; set; } = string.Empty;
    }
}

