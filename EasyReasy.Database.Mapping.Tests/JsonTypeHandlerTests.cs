using System.Data;
using System.Text.Json;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JsonTypeHandler{T}"/>. Covers POCO, dictionary,
    /// and list payloads since those are the common JSON-column shapes.
    /// </summary>
    public class JsonTypeHandlerTests
    {
        [Fact]
        public void Constructor_DefaultOptions_RoundTripsPoco()
        {
            JsonTypeHandler<Address> handler = new JsonTypeHandler<Address>();
            FakeDbParameter parameter = new FakeDbParameter();

            handler.SetValue(parameter, new Address { City = "Stockholm", PostalCode = "11122" });
            Address? loaded = handler.Parse(parameter.Value!);

            Assert.NotNull(loaded);
            Assert.Equal("Stockholm", loaded!.City);
            Assert.Equal("11122", loaded.PostalCode);
        }

        [Fact]
        public void Constructor_CustomOptions_HonorsNamingPolicy()
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            JsonTypeHandler<Address> handler = new JsonTypeHandler<Address>(options);
            FakeDbParameter parameter = new FakeDbParameter();

            handler.SetValue(parameter, new Address { City = "Stockholm", PostalCode = "11122" });

            string serialized = (string)parameter.Value!;
            Assert.Contains("\"city\":", serialized);
            Assert.Contains("\"postalCode\":", serialized);
        }

        [Fact]
        public void SetValue_SerializesAsString_WithDbTypeString()
        {
            JsonTypeHandler<Address> handler = new JsonTypeHandler<Address>();
            FakeDbParameter parameter = new FakeDbParameter();

            handler.SetValue(parameter, new Address { City = "Oslo", PostalCode = "0150" });

            Assert.IsType<string>(parameter.Value);
            Assert.Equal(DbType.String, parameter.DbType);
        }

        [Fact]
        public void Parse_StringInput_Deserializes()
        {
            JsonTypeHandler<Address> handler = new JsonTypeHandler<Address>();

            Address? loaded = handler.Parse("{\"City\":\"Helsinki\",\"PostalCode\":\"00100\"}");

            Assert.NotNull(loaded);
            Assert.Equal("Helsinki", loaded!.City);
            Assert.Equal("00100", loaded.PostalCode);
        }

        [Fact]
        public void Parse_NonStringInput_UsesToString()
        {
            // Mirrors PolymorphicJsonTypeHandler — anything boxed as object
            // (e.g. a JsonElement) falls through to .ToString() so providers
            // returning non-string JSON values still round-trip.
            JsonTypeHandler<Address> handler = new JsonTypeHandler<Address>();
            object value = new BoxedJson("{\"City\":\"Reykjavik\",\"PostalCode\":\"101\"}");

            Address? loaded = handler.Parse(value);

            Assert.NotNull(loaded);
            Assert.Equal("Reykjavik", loaded!.City);
        }

        [Fact]
        public void Parse_NullInput_ThrowsArgumentNullException()
        {
            JsonTypeHandler<Address> handler = new JsonTypeHandler<Address>();

            Assert.Throws<ArgumentNullException>(() => handler.Parse(null!));
        }

        [Fact]
        public void RoundTrip_Dictionary_PreservesEntries()
        {
            // Dictionary shape — the canonical "small bag of context" use case
            // (BroadcastAction.Context-style).
            JsonTypeHandler<Dictionary<string, string>> handler =
                new JsonTypeHandler<Dictionary<string, string>>();
            FakeDbParameter parameter = new FakeDbParameter();

            Dictionary<string, string> original = new Dictionary<string, string>
            {
                ["coverId"] = "abc-123",
                ["correlationId"] = "xyz-789",
            };

            handler.SetValue(parameter, original);
            Dictionary<string, string>? loaded = handler.Parse(parameter.Value!);

            Assert.NotNull(loaded);
            Assert.Equal("abc-123", loaded!["coverId"]);
            Assert.Equal("xyz-789", loaded["correlationId"]);
        }

        [Fact]
        public void RoundTrip_List_PreservesOrder()
        {
            JsonTypeHandler<List<int>> handler = new JsonTypeHandler<List<int>>();
            FakeDbParameter parameter = new FakeDbParameter();

            handler.SetValue(parameter, new List<int> { 3, 1, 4, 1, 5, 9, 2, 6 });
            List<int>? loaded = handler.Parse(parameter.Value!);

            Assert.NotNull(loaded);
            Assert.Equal(new[] { 3, 1, 4, 1, 5, 9, 2, 6 }, loaded);
        }

        #region Test types

        public class Address
        {
            public string City { get; set; } = string.Empty;
            public string PostalCode { get; set; } = string.Empty;
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
