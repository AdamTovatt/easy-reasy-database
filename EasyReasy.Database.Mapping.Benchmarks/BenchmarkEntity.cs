namespace EasyReasy.Database.Mapping.Benchmarks
{
    public class BenchmarkEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public decimal Score { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Description { get; set; }
    }
}
