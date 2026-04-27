using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Unit tests for <see cref="PolymorphicJsonTypeHandler{TBase}"/>.
    /// </summary>
    public class PolymorphicJsonTypeHandlerTests
    {
        [Fact]
        public void Constructor_DefaultOptions_ParsesDiscriminatorLastJson()
        {
            PolymorphicJsonTypeHandler<Shape> handler = new PolymorphicJsonTypeHandler<Shape>();

            // Default options use PascalCase, so use PascalCase property names here.
            string json = "{\"Radius\":5,\"$type\":\"circle\"}";

            Shape? result = handler.Parse(json);

            Circle circle = Assert.IsType<Circle>(result);
            Assert.Equal(5, circle.Radius);
        }

        [Fact]
        public void Constructor_CustomOptions_HonorsNamingPolicy()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            PolymorphicJsonTypeHandler<Shape> handler = new PolymorphicJsonTypeHandler<Shape>(options);

            string json = "{\"width\":3,\"height\":4,\"$type\":\"rectangle\"}";

            Shape? result = handler.Parse(json);

            Rectangle rect = Assert.IsType<Rectangle>(result);
            Assert.Equal(3, rect.Width);
            Assert.Equal(4, rect.Height);
        }

        [Fact]
        public void Constructor_OptionsAlreadyHasConverter_ParsesWithoutDuplicateError()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.Converters.Add(new OrderInsensitivePolymorphicJsonConverter<Shape>());

            // The handler copies the options and applies Configure on the copy. Configure's
            // dedup logic should keep the converter list at exactly one entry.
            PolymorphicJsonTypeHandler<Shape> handler = new PolymorphicJsonTypeHandler<Shape>(options);

            string json = "{\"Radius\":5,\"$type\":\"circle\"}";
            Shape? result = handler.Parse(json);

            Assert.IsType<Circle>(result);
        }

        [Fact]
        public void Constructor_SuppliedOptions_AreNotMutated()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            int convertersBefore = options.Converters.Count;
            object? resolverBefore = options.TypeInfoResolver;

            _ = new PolymorphicJsonTypeHandler<Shape>(options);

            Assert.Equal(convertersBefore, options.Converters.Count);
            Assert.Same(resolverBefore, options.TypeInfoResolver);
        }

        [Fact]
        public void SetValue_SerializesAsString_WithDbTypeString()
        {
            PolymorphicJsonTypeHandler<Shape> handler = new PolymorphicJsonTypeHandler<Shape>();
            FakeDbParameter parameter = new FakeDbParameter();

            handler.SetValue(parameter, new Circle { Radius = 7 });

            string serialized = Assert.IsType<string>(parameter.Value);
            Assert.StartsWith("{\"$type\":\"circle\"", serialized);
            Assert.Equal(DbType.String, parameter.DbType);
        }

        [Fact]
        public void SetValue_ThenParse_RoundTripsValue()
        {
            PolymorphicJsonTypeHandler<Shape> handler = new PolymorphicJsonTypeHandler<Shape>();
            FakeDbParameter parameter = new FakeDbParameter();

            handler.SetValue(parameter, new Rectangle { Width = 11, Height = 22 });

            Shape? loaded = handler.Parse(parameter.Value!);

            Rectangle rect = Assert.IsType<Rectangle>(loaded);
            Assert.Equal(11, rect.Width);
            Assert.Equal(22, rect.Height);
        }

        [Fact]
        public void Parse_NonStringInput_UsesToString()
        {
            PolymorphicJsonTypeHandler<Shape> handler = new PolymorphicJsonTypeHandler<Shape>();
            // Simulates the case where a provider hands us something that isn't already
            // a string (e.g. a JsonElement boxed as object).
            object value = new BoxedJson("{\"Radius\":1,\"$type\":\"circle\"}");

            Shape? result = handler.Parse(value);

            Circle circle = Assert.IsType<Circle>(result);
            Assert.Equal(1, circle.Radius);
        }

        [Fact]
        public void Parse_NullInput_ThrowsArgumentNullException()
        {
            PolymorphicJsonTypeHandler<Shape> handler = new PolymorphicJsonTypeHandler<Shape>();

            Assert.Throws<ArgumentNullException>(() => handler.Parse(null!));
        }

        [Fact]
        public void RoundTrip_DiscriminatorFirstAfterReorder_StillReadable()
        {
            // Simulates JSONB's length-first key ordering: we serialize, manually reorder
            // the keys so the discriminator is last, and verify the handler still parses.
            PolymorphicJsonTypeHandler<Shape> handler = new PolymorphicJsonTypeHandler<Shape>();
            FakeDbParameter parameter = new FakeDbParameter();
            handler.SetValue(parameter, new Rectangle { Width = 9, Height = 16 });

            string serialized = (string)parameter.Value!;
            string reordered = ReorderDiscriminatorToEnd(serialized, "$type");

            Shape? result = handler.Parse(reordered);

            Rectangle rect = Assert.IsType<Rectangle>(result);
            Assert.Equal(9, rect.Width);
            Assert.Equal(16, rect.Height);
        }

        private static string ReorderDiscriminatorToEnd(string json, string discriminatorName)
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                JsonElement? discriminator = null;
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals(discriminatorName))
                    {
                        discriminator = prop.Value.Clone();
                        continue;
                    }
                    prop.WriteTo(writer);
                }
                if (discriminator.HasValue)
                {
                    writer.WritePropertyName(discriminatorName);
                    discriminator.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        #region Test types

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
        [JsonDerivedType(typeof(Circle), "circle")]
        [JsonDerivedType(typeof(Rectangle), "rectangle")]
        public abstract class Shape
        {
        }

        public class Circle : Shape
        {
            public int Radius { get; set; }
        }

        public class Rectangle : Shape
        {
            public int Width { get; set; }
            public int Height { get; set; }
        }

        private sealed class BoxedJson
        {
            private readonly string _value;
            public BoxedJson(string value) { _value = value; }
            public override string ToString() => _value;
        }

        private sealed class FakeDbParameter : IDbDataParameter
        {
            public byte Precision { get; set; }
            public byte Scale { get; set; }
            public int Size { get; set; }
            public DbType DbType { get; set; }
            public ParameterDirection Direction { get; set; }
            public bool IsNullable => true;
#pragma warning disable CS8766, CS8767 // Nullable annotations on IDataParameter implementations vary across framework versions.
            public string? ParameterName { get; set; } = string.Empty;
            public string? SourceColumn { get; set; } = string.Empty;
#pragma warning restore CS8766, CS8767
            public DataRowVersion SourceVersion { get; set; }
            public object? Value { get; set; }
        }

        #endregion
    }
}
