using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Unit tests for <see cref="OrderInsensitivePolymorphicJsonConverter{TBase}"/>.
    /// These tests do not require a database — the bug is at the System.Text.Json layer
    /// and is provider-agnostic.
    /// </summary>
    public class PolymorphicJsonConverterTests
    {
        private static JsonSerializerOptions BuildOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);
            return options;
        }

        private static JsonSerializerOptions BuildIntDiscriminatorOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            OrderInsensitivePolymorphicJsonConverter<NumericAnimal>.Configure(options);
            return options;
        }

        [Fact]
        public void Read_DiscriminatorFirst_Deserializes()
        {
            string json = "{\"type\":\"dog\",\"name\":\"Rex\",\"breed\":\"lab\"}";

            Animal? result = JsonSerializer.Deserialize<Animal>(json, BuildOptions());

            Dog dog = Assert.IsType<Dog>(result);
            Assert.Equal("Rex", dog.Name);
            Assert.Equal("lab", dog.Breed);
        }

        [Fact]
        public void Read_DiscriminatorLast_Deserializes()
        {
            // The JSONB case: a 4-char "type" gets pushed past 5+ char properties.
            string json = "{\"name\":\"Rex\",\"breed\":\"lab\",\"type\":\"dog\"}";

            Animal? result = JsonSerializer.Deserialize<Animal>(json, BuildOptions());

            Dog dog = Assert.IsType<Dog>(result);
            Assert.Equal("Rex", dog.Name);
            Assert.Equal("lab", dog.Breed);
        }

        [Fact]
        public void Read_DiscriminatorMiddle_Deserializes()
        {
            string json = "{\"name\":\"Whiskers\",\"type\":\"cat\",\"color\":\"black\"}";

            Animal? result = JsonSerializer.Deserialize<Animal>(json, BuildOptions());

            Cat cat = Assert.IsType<Cat>(result);
            Assert.Equal("Whiskers", cat.Name);
            Assert.Equal("black", cat.Color);
        }

        [Fact]
        public void Read_DefaultStjFails_OnDiscriminatorLast()
        {
            // Verifies the bug we are fixing actually exists in default STJ.
            string json = "{\"name\":\"Rex\",\"breed\":\"lab\",\"type\":\"dog\"}";

            JsonSerializerOptions defaultOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            Assert.Throws<NotSupportedException>(() =>
                JsonSerializer.Deserialize<Animal>(json, defaultOptions));
        }

        [Fact]
        public void Read_DiscriminatorMissing_ThrowsJsonException()
        {
            string json = "{\"name\":\"Rex\",\"breed\":\"lab\"}";

            JsonException ex = Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<Animal>(json, BuildOptions()));

            Assert.Contains("Discriminator property 'type' not found", ex.Message);
        }

        [Fact]
        public void Read_DiscriminatorValueUnknown_ThrowsJsonException()
        {
            string json = "{\"type\":\"horse\",\"name\":\"Spirit\"}";

            JsonException ex = Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<Animal>(json, BuildOptions()));

            Assert.Contains("did not match any [JsonDerivedType]", ex.Message);
        }

        [Fact]
        public void Configure_NotPolymorphicallyConfigured_ThrowsInvalidOperationException()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();

            // Lazy<T> wraps the metadata read, so the underlying InvalidOperationException
            // surfaces directly from the first Deserialize rather than as
            // TypeInitializationException.
            OrderInsensitivePolymorphicJsonConverter<NotPolymorphic>.Configure(options);

            string json = "{\"value\":1}";

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                JsonSerializer.Deserialize<NotPolymorphic>(json, options));

            Assert.Contains("[JsonPolymorphic]", ex.Message);
        }

        [Fact]
        public void Configure_DerivedTypeWithoutDiscriminator_ThrowsInvalidOperationException()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();

            OrderInsensitivePolymorphicJsonConverter<MissingDiscriminatorBase>.Configure(options);

            // First Deserialize triggers metadata build via Lazy<T>.
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                JsonSerializer.Deserialize<MissingDiscriminatorBase>("{}", options));

            Assert.Contains("without an explicit discriminator", ex.Message);
        }

        [Fact]
        public void Configure_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(null!));
        }

        [Fact]
        public void Configure_CalledTwice_AddsConverterAndModifierOnlyOnce()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();

            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);
            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);

            int converters = 0;
            foreach (JsonConverter c in options.Converters)
            {
                if (c is OrderInsensitivePolymorphicJsonConverter<Animal>)
                {
                    converters++;
                }
            }
            Assert.Equal(1, converters);

            DefaultJsonTypeInfoResolver resolver = Assert.IsType<DefaultJsonTypeInfoResolver>(options.TypeInfoResolver);
            Assert.Single(resolver.Modifiers);
        }

        [Fact]
        public void Configure_PreservesCustomResolver_ByWrappingIt()
        {
            // A custom resolver that's not a DefaultJsonTypeInfoResolver — Configure should
            // wrap it rather than overwrite, so its results still flow through.
            CountingResolver custom = new CountingResolver(new DefaultJsonTypeInfoResolver());
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                TypeInfoResolver = custom,
            };

            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);

            // Drive a deserialize that exercises the resolver chain.
            string json = "{\"name\":\"Rex\",\"breed\":\"lab\",\"type\":\"dog\"}";
            Animal? result = JsonSerializer.Deserialize<Animal>(json, options);

            Assert.IsType<Dog>(result);
            Assert.True(custom.Calls > 0, "Custom resolver should still be consulted after Configure wraps it.");
        }

        [Fact]
        public void Configure_CalledTwice_WithCustomResolver_DoesNotStackWrappers()
        {
            CountingResolver custom = new CountingResolver(new DefaultJsonTypeInfoResolver());
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                TypeInfoResolver = custom,
            };

            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);
            IJsonTypeInfoResolver afterFirst = options.TypeInfoResolver!;
            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);
            IJsonTypeInfoResolver afterSecond = options.TypeInfoResolver!;

            Assert.Same(afterFirst, afterSecond);

            // And the second call still produces a working chain.
            string json = "{\"name\":\"Rex\",\"breed\":\"lab\",\"type\":\"dog\"}";
            Animal? result = JsonSerializer.Deserialize<Animal>(json, options);
            Assert.IsType<Dog>(result);
        }

        [Fact]
        public void Configure_TwoBaseTypes_WithCustomResolver_ShareSingleWrapper()
        {
            CountingResolver custom = new CountingResolver(new DefaultJsonTypeInfoResolver());
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                TypeInfoResolver = custom,
            };

            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);
            IJsonTypeInfoResolver afterFirst = options.TypeInfoResolver!;
            OrderInsensitivePolymorphicJsonConverter<NumericAnimal>.Configure(options);
            IJsonTypeInfoResolver afterSecond = options.TypeInfoResolver!;

            // Both base types are accumulated into the same wrapper instance.
            Assert.Same(afterFirst, afterSecond);

            // And both still deserialize correctly.
            Animal? animal = JsonSerializer.Deserialize<Animal>(
                "{\"name\":\"Rex\",\"breed\":\"lab\",\"type\":\"dog\"}", options);
            NumericAnimal? numeric = JsonSerializer.Deserialize<NumericAnimal>(
                "{\"name\":\"Rex\",\"speed\":42,\"kind\":1}", options);

            Assert.IsType<Dog>(animal);
            Assert.IsType<NumericDog>(numeric);
        }

        [Fact]
        public void Read_PropertyNameCaseInsensitive_MatchesDiscriminatorRegardlessOfCase()
        {
            // STJ's built-in polymorphism honors PropertyNameCaseInsensitive when looking up
            // the discriminator. Match that behavior so consumers can swap converters without
            // surprises.
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            };
            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);

            string json = "{\"NAME\":\"Rex\",\"BREED\":\"lab\",\"TYPE\":\"dog\"}";

            Animal? result = JsonSerializer.Deserialize<Animal>(json, options);

            Dog dog = Assert.IsType<Dog>(result);
            Assert.Equal("Rex", dog.Name);
            Assert.Equal("lab", dog.Breed);
        }

        [Fact]
        public void Read_PropertyNameCaseSensitive_DoesNotMatchUppercaseDiscriminator()
        {
            // Default (case-sensitive) options should NOT match a different-cased discriminator.
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            OrderInsensitivePolymorphicJsonConverter<Animal>.Configure(options);

            string json = "{\"name\":\"Rex\",\"breed\":\"lab\",\"TYPE\":\"dog\"}";

            JsonException ex = Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<Animal>(json, options));

            Assert.Contains("Discriminator property 'type' not found", ex.Message);
        }

        [Fact]
        public void Read_NumericDiscriminatorLast_Deserializes()
        {
            string json = "{\"name\":\"Rex\",\"speed\":42,\"kind\":1}";

            NumericAnimal? result = JsonSerializer.Deserialize<NumericAnimal>(json, BuildIntDiscriminatorOptions());

            NumericDog dog = Assert.IsType<NumericDog>(result);
            Assert.Equal("Rex", dog.Name);
            Assert.Equal(42, dog.Speed);
        }

        [Fact]
        public void Read_Null_ReturnsNull()
        {
            string json = "null";

            Animal? result = JsonSerializer.Deserialize<Animal>(json, BuildOptions());

            Assert.Null(result);
        }

        [Fact]
        public void Read_NotAnObject_ThrowsJsonException()
        {
            string json = "[1,2,3]";

            JsonException ex = Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<Animal>(json, BuildOptions()));

            Assert.Contains("Expected StartObject", ex.Message);
        }

        [Fact]
        public void Write_EmitsDiscriminatorFirst()
        {
            Dog dog = new Dog { Name = "Rex", Breed = "lab" };

            string json = JsonSerializer.Serialize<Animal>(dog, BuildOptions());

            // Discriminator must be the first key for default-STJ interop.
            Assert.StartsWith("{\"type\":\"dog\"", json);
        }

        [Fact]
        public void Write_ConcretePolymorphicBaseInstance_ThrowsJsonException()
        {
            // A concrete (non-abstract) [JsonPolymorphic] base whose own type is not in its
            // [JsonDerivedType] list. Serializing an instance of TBase directly would
            // re-enter the converter; the recursion guard turns that into a clear error.
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            OrderInsensitivePolymorphicJsonConverter<ConcreteBase>.Configure(options);

            ConcreteBase value = new ConcreteBase { Name = "Direct" };

            JsonException ex = Assert.Throws<JsonException>(() =>
                JsonSerializer.Serialize<ConcreteBase>(value, options));

            Assert.Contains("polymorphic base type", ex.Message);
        }

        [Fact]
        public void Write_UnregisteredRuntimeType_FallsThroughWithoutDiscriminator()
        {
            // Grandchild types (or runtime types not declared via [JsonDerivedType]) fall
            // through to default System.Text.Json serialization with no discriminator.
            Mutt mutt = new Mutt { Name = "Buddy", Breed = "mix", Tag = "t-1" };

            string json = JsonSerializer.Serialize<Animal>(mutt, BuildOptions());

            Assert.DoesNotContain("\"type\"", json);
            Assert.Contains("\"name\":\"Buddy\"", json);
            Assert.Contains("\"breed\":\"mix\"", json);
            Assert.Contains("\"tag\":\"t-1\"", json);
        }

        [Fact]
        public void RoundTrip_DiscriminatorFirst_PreservesData()
        {
            Cat cat = new Cat { Name = "Whiskers", Color = "black" };

            string json = JsonSerializer.Serialize<Animal>(cat, BuildOptions());
            Animal? result = JsonSerializer.Deserialize<Animal>(json, BuildOptions());

            Cat parsed = Assert.IsType<Cat>(result);
            Assert.Equal("Whiskers", parsed.Name);
            Assert.Equal("black", parsed.Color);
        }

        [Fact]
        public void RoundTrip_NumericDiscriminator_PreservesData()
        {
            NumericDog dog = new NumericDog { Name = "Rex", Speed = 42 };

            string json = JsonSerializer.Serialize<NumericAnimal>(dog, BuildIntDiscriminatorOptions());
            NumericAnimal? result = JsonSerializer.Deserialize<NumericAnimal>(json, BuildIntDiscriminatorOptions());

            Assert.StartsWith("{\"kind\":1", json);
            NumericDog parsed = Assert.IsType<NumericDog>(result);
            Assert.Equal("Rex", parsed.Name);
            Assert.Equal(42, parsed.Speed);
        }

        [Fact]
        public void Write_NullValue_WritesJsonNull()
        {
            string json = JsonSerializer.Serialize<Animal?>(null, BuildOptions());

            Assert.Equal("null", json);
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

        // Grandchild — not declared as [JsonDerivedType] on Animal.
        public class Mutt : Dog
        {
            public string Tag { get; set; } = string.Empty;
        }

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
        [JsonDerivedType(typeof(NumericDog), 1)]
        [JsonDerivedType(typeof(NumericCat), 2)]
        public abstract class NumericAnimal
        {
            public string Name { get; set; } = string.Empty;
        }

        public class NumericDog : NumericAnimal
        {
            public int Speed { get; set; }
        }

        public class NumericCat : NumericAnimal
        {
            public int Lives { get; set; }
        }

        public class NotPolymorphic
        {
            public int Value { get; set; }
        }

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
        [JsonDerivedType(typeof(MissingDiscriminatorChild))]
        public abstract class MissingDiscriminatorBase
        {
        }

        public class MissingDiscriminatorChild : MissingDiscriminatorBase
        {
        }

        // Non-abstract polymorphic base — used to exercise the recursion guard.
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
        [JsonDerivedType(typeof(ConcreteChild), "child")]
        public class ConcreteBase
        {
            public string Name { get; set; } = string.Empty;
        }

        public class ConcreteChild : ConcreteBase
        {
            public string Extra { get; set; } = string.Empty;
        }

        private sealed class CountingResolver : IJsonTypeInfoResolver
        {
            private readonly IJsonTypeInfoResolver _inner;
            public int Calls { get; private set; }

            public CountingResolver(IJsonTypeInfoResolver inner)
            {
                _inner = inner;
            }

            public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
            {
                Calls++;
                return _inner.GetTypeInfo(type, options);
            }
        }

        #endregion
    }
}
