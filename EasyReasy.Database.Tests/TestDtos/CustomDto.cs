using EasyReasy.Database.Sorting;

namespace EasyReasy.Database.Tests.TestDtos
{
    public class CustomDto
    {
        [Sortable(columnName: "custom_age", isDefault: true)]
        public int Age { get; set; }
    }
}

