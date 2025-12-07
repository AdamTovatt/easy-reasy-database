using EasyReasy.Database.Sorting;

namespace EasyReasy.Database.Tests.TestDtos
{
    public class NoDefaultDto
    {
        [Sortable]
        public string Name { get; set; } = string.Empty;
    }
}

