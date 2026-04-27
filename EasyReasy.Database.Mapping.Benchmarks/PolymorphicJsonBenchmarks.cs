using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;

namespace EasyReasy.Database.Mapping.Benchmarks
{
    /// <summary>
    /// Measures the cost of <see cref="OrderInsensitivePolymorphicJsonConverter{TBase}"/>
    /// against default <see cref="System.Text.Json"/> polymorphic deserialization.
    /// The converter materializes a <see cref="JsonDocument"/> to find the discriminator
    /// anywhere in the object — typically ~2× the cost of the discriminator-first happy path.
    /// </summary>
    [MemoryDiagnoser]
    public class PolymorphicJsonBenchmarks
    {
        private byte[] _discriminatorFirst = null!;
        private byte[] _discriminatorLast = null!;
        private JsonSerializerOptions _defaultOptions = null!;
        private JsonSerializerOptions _converterOptions = null!;

        [GlobalSetup]
        public void Setup()
        {
            _defaultOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            _converterOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(_converterOptions);

            _discriminatorFirst = Encoding.UTF8.GetBytes(
                "{\"type\":\"dog\",\"name\":\"Rex\",\"breed\":\"labrador\"}");

            _discriminatorLast = Encoding.UTF8.GetBytes(
                "{\"name\":\"Rex\",\"breed\":\"labrador\",\"type\":\"dog\"}");
        }

        [Benchmark(Baseline = true)]
        public Animal? DefaultStj_DiscriminatorFirst()
        {
            return JsonSerializer.Deserialize<Animal>(_discriminatorFirst, _defaultOptions);
        }

        [Benchmark]
        public Animal? Converter_DiscriminatorFirst()
        {
            return JsonSerializer.Deserialize<Animal>(_discriminatorFirst, _converterOptions);
        }

        [Benchmark]
        public Animal? Converter_DiscriminatorLast()
        {
            return JsonSerializer.Deserialize<Animal>(_discriminatorLast, _converterOptions);
        }

        #region Test types

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
        [JsonDerivedType(typeof(Dog), "dog")]
        [JsonDerivedType(typeof(Cat), "cat")]
        public abstract class Animal
        {
            public string Name { get; set; } = string.Empty;
        }

        public class Dog : Animal
        {
            public string Breed { get; set; } = string.Empty;
        }

        public class Cat : Animal
        {
            public string Color { get; set; } = string.Empty;
        }

        #endregion
    }
}
