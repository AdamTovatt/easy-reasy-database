# EasyReasy.Database.Mapping

A lightweight SQL-to-object mapping library that provides extension methods on `DbConnection` for executing queries and deserializing results. Designed as a focused replacement for Dapper with three improvements over Dapper:

- **Type handlers that actually work for enums** - Dapper silently ignores registered handlers for enum types. This library checks the handler registry first.
- **Automatic snake_case column mapping** - PostgreSQL columns like `created_at` map to `CreatedAt` properties without `AS` aliases.
- **Constructor-based entity creation** - Entities can use constructors instead of requiring a parameterless constructor with settable properties, enabling proper non-nullable reference type (NRT) support.

## Why This Exists

Dapper has a [well-known bug](https://github.com/DapperLib/Dapper/issues/259) (open since 2015) where registered type handlers are silently ignored for enum types. Dapper's fast path converts enums to integers before checking for handlers, so `AddTypeHandler<MyEnum>(handler)` has no effect. This library checks the type handler registry **before** any default conversion, which is the core fix.

## Usage with EasyReasy.Database

This library is designed to work with `EasyReasy.Database`'s session and repository patterns. Replace `using Dapper;` with `using EasyReasy.Database.Mapping;` and everything works the same way:

```csharp
using EasyReasy.Database.Mapping;

public class CustomerRepository : RepositoryBase, ICustomerRepository
{
    public async Task<Customer?> GetByIdAsync(Guid id, IDbSession? session = null)
    {
        return await UseSessionAsync(async (dbSession) =>
        {
            string query = $@"SELECT id, name, status FROM customer WHERE id = @{nameof(id)}";

            return await dbSession.Connection.QuerySingleOrDefaultAsync<Customer>(
                query,
                new { id },
                transaction: dbSession.Transaction);
        }, session);
    }
}
```

## API Reference

All methods are async extension methods on `DbConnection`. Parameters are passed as anonymous objects or `DynamicParameters`.

### QueryAsync

Returns multiple rows deserialized into `T`.

```csharp
IEnumerable<Customer> customers = await connection.QueryAsync<Customer>(
    "SELECT id, name FROM customer WHERE active = @active",
    new { active = true },
    transaction);
```

### QuerySingleAsync

Returns exactly one row. Throws `InvalidOperationException` if zero or more than one row is returned.

```csharp
Customer customer = await connection.QuerySingleAsync<Customer>(
    "SELECT id, name FROM customer WHERE id = @id",
    new { id },
    transaction);
```

### QuerySingleOrDefaultAsync

Returns one row or `default` if no rows. Throws `InvalidOperationException` if more than one row is returned.

```csharp
Customer? customer = await connection.QuerySingleOrDefaultAsync<Customer>(
    "SELECT id, name FROM customer WHERE id = @id",
    new { id },
    transaction);
```

### QueryFirstOrDefaultAsync

Returns the first row or `default` if no rows. Unlike `QuerySingleOrDefaultAsync`, does not throw when multiple rows are returned.

```csharp
Customer? customer = await connection.QueryFirstOrDefaultAsync<Customer>(
    "SELECT id, name FROM customer WHERE active = @active",
    new { active = true },
    transaction);
```

### ExecuteAsync

Executes a non-query command (INSERT, UPDATE, DELETE). Returns the number of rows affected.

```csharp
int rowsAffected = await connection.ExecuteAsync(
    "UPDATE customer SET name = @name WHERE id = @id",
    new { id, name },
    transaction);
```

### ExecuteScalarAsync

Returns the first column of the first row.

```csharp
long count = await connection.ExecuteScalarAsync<long>(
    "SELECT COUNT(*) FROM customer WHERE active = @active",
    new { active = true },
    transaction);
```

### QueryMultipleAsync

Executes a query with multiple result sets. Returns a `GridReader` for reading them sequentially.

```csharp
await using GridReader grid = await connection.QueryMultipleAsync(
    @"SELECT COUNT(*) FROM customer;
      SELECT id, name FROM customer ORDER BY name LIMIT @limit",
    new { limit = 10 },
    transaction);

long count = await grid.ReadSingleAsync<long>();
IEnumerable<Customer> customers = await grid.ReadAsync<Customer>();
```

## Type Handlers

Register custom type handlers to control how values are read from and written to the database. Unlike Dapper, handlers registered for enum types are correctly invoked.

### Enum Type Handlers

Use the `[DbName]` attribute on enum fields and register a `DbNameEnumHandler<T>` to automatically map between enum values and their database string representations.

For databases that use enum types (e.g. PostgreSQL), you can also add the `[DbEnum]` attribute on the enum type itself to specify the database enum type name. This is used by provider-specific extensions like `MapDbNameEnum` in [EasyReasy.Database.Mapping.Npgsql](../EasyReasy.Database.Mapping.Npgsql/README.md) to simplify registration.

```csharp
[DbEnum("customer_status")]  // optional (but recommended) - database enum type name, used by provider-specific extensions (like EasyReasy.Database.Mapping.Npgsql)
public enum CustomerStatus
{
    [DbName("active")]
    Active,

    [DbName("inactive")]
    Inactive
}
```

```csharp
TypeHandlerRegistry.AddTypeHandler(new DbNameEnumHandler<CustomerStatus>());
```

Every field on the enum must have a `[DbName]` attribute. The handler builds a bidirectional lookup at construction time and throws `InvalidOperationException` if any field is missing the attribute.

Once registered, enum values are handled automatically in queries:

```csharp
// Writing  - the handler converts CustomerStatus.Active to "active"
await connection.ExecuteAsync(
    "INSERT INTO customer (name, status) VALUES (@name, @status::customer_status)",
    new { name = "Acme", status = CustomerStatus.Active },
    transaction);

// Reading  - the handler converts "active" back to CustomerStatus.Active
Customer? customer = await connection.QuerySingleOrDefaultAsync<Customer>(
    "SELECT name, status FROM customer WHERE id = @id",
    new { id },
    transaction);
```

### Custom Type Handlers

For non-enum types, subclass `TypeHandler<T>` to control how values are written to and read from the database. For example, a handler that compresses large strings before storing them:

```csharp
public class GzipStringHandler : TypeHandler<string>
{
    public override void SetValue(IDbDataParameter parameter, string value)
    {
        parameter.Value = Compress(value);   // returns a base64 string
        parameter.DbType = DbType.String;
    }

    public override string? Parse(object value)
    {
        return Decompress((string)value);
    }
}
```

```csharp
TypeHandlerRegistry.AddTypeHandler(new GzipStringHandler());
```

Once registered, handlers are used automatically for both parameter binding (writes) and result deserialization (reads). For JSON-backed columns specifically, the library ships built-in handlers — see [JSON columns](#json-columns) below.

### JSON columns

Two built-in handlers cover persisting CLR objects in JSON or JSONB columns:

- `JsonTypeHandler<T>` — for plain types (POCOs, records, dictionaries, lists, anything that round-trips through `JsonSerializer` with default behavior).
- `PolymorphicJsonTypeHandler<TBase>` — for `[JsonPolymorphic]` hierarchies; additionally handles the JSONB key-reorder discriminator issue described in [Polymorphic JSON (order-insensitive)](#polymorphic-json-order-insensitive) below.

```csharp
TypeHandlerRegistry.AddTypeHandler(new JsonTypeHandler<Address>());
TypeHandlerRegistry.AddTypeHandler(new JsonTypeHandler<Dictionary<string, string>>());
```

Both handlers accept an optional `JsonSerializerOptions` for naming policies, additional converters, etc.:

```csharp
JsonSerializerOptions options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
TypeHandlerRegistry.AddTypeHandler(new JsonTypeHandler<Address>(options));
```

`JsonTypeHandler<T>` uses the supplied options instance as-is — no internal copy or freeze — so caller mutations after construction take effect on subsequent reads/writes. (`PolymorphicJsonTypeHandler<TBase>` copies and freezes its options because the polymorphism setup is fragile; see the next section.)

When reading, columns must be cast to text (`column::text AS column`) so the handler receives a JSON string — the row deserializer dispatches the registered handler on the property's CLR type and feeds it the column value.

### Polymorphic JSON (order-insensitive)

`System.Text.Json`'s built-in polymorphic deserialization (`[JsonPolymorphic]` + `[JsonDerivedType]`) requires the discriminator property to be the first key in the object and throws `NotSupportedException` otherwise (see [dotnet/runtime#78338](https://github.com/dotnet/runtime/issues/78338)). PostgreSQL JSONB stores keys length-first then lexicographically, so on read-back the discriminator is often *not* first — even though it was when you serialized. Today this lurks until someone adds a property like `body`, `data`, `name`, or `tags` (≤4 chars), at which point production starts throwing.

`PolymorphicJsonTypeHandler<TBase>` fixes this with one line of registration:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ImageBlock), "image")]
public abstract class ContentBlock { /* ... */ }

TypeHandlerRegistry.AddTypeHandler(new PolymorphicJsonTypeHandler<ContentBlock>());
```

`TBase` must be a reference type. If `[JsonPolymorphic]` does not specify `TypeDiscriminatorPropertyName`, the converter uses the same default as `System.Text.Json` (`"$type"`). Every `[JsonDerivedType]` must carry an explicit string or int discriminator — `[JsonDerivedType(typeof(X))]` without one is rejected at registration time with a clear error (the converter does not infer assembly-qualified-name discriminators).

Reads work regardless of where the discriminator appears in the JSON. Writes still emit the discriminator first, so the output is interoperable with default `System.Text.Json` deserialization. Runtime types not declared via `[JsonDerivedType]` (e.g. grandchild subclasses) fall through to the default serializer with no discriminator written — match `System.Text.Json`'s own behavior.

The handler accepts an optional `JsonSerializerOptions` for naming policies, additional converters, etc.:

```csharp
TypeHandlerRegistry.AddTypeHandler(new PolymorphicJsonTypeHandler<ContentBlock>(
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
```

#### Standalone converter

For consumers that don't use the Mapping registry — anyone with a `JsonSerializerOptions` instance — the underlying `OrderInsensitivePolymorphicJsonConverter<TBase>` can be applied directly:

```csharp
JsonSerializerOptions options = new JsonSerializerOptions();
OrderInsensitivePolymorphicJsonConverter<ContentBlock>.Configure(options);

ContentBlock? block = JsonSerializer.Deserialize<ContentBlock>(json, options);
```

`Configure` adds the converter and installs a type-info modifier that clears `JsonTypeInfo.PolymorphismOptions` for the base type — `System.Text.Json` rejects custom converters on `[JsonPolymorphic]` types because they can't participate in its built-in metadata flow, so this converter handles dispatch end-to-end. `Configure` is idempotent but not thread-safe; call it once at startup. If `options.TypeInfoResolver` is already set to a custom resolver (e.g. a source-generated `JsonSerializerContext`), `Configure` wraps it; the modifier still runs after the inner resolver returns each `JsonTypeInfo`.

#### Performance

The converter parses the value into a `JsonDocument` to find the discriminator anywhere in the object, adding a small constant-factor overhead vs. `System.Text.Json`'s discriminator-first happy path. For typical JSONB column values (KB-scale content blocks, file descriptions, etc.) this is microseconds — invisible. For multi-MB blobs or hot bulk-deserialization loops the cost is measurable; benchmarks live in `EasyReasy.Database.Mapping.Benchmarks` (`PolymorphicJsonBenchmarks` class) — run them on your target hardware to make decisions.

## Column Mapping

Result columns are mapped to entity properties by name using case-insensitive matching. The following column naming conventions all work automatically:

| SQL column | Entity property | How it matches |
|---|---|---|
| `Name` | `Name` | Direct match |
| `name` | `Name` | Case-insensitive (PostgreSQL default) |
| `created_at` | `CreatedAt` | Snake_case to PascalCase conversion |
| `is_active` | `IsActive` | Snake_case to PascalCase conversion |

Direct matching is tried first. Snake_case conversion only runs when a column doesn't match any property directly, so there's no overhead for queries that already use aliases or matching names.

This means you can write natural SQL without `AS` aliases:

```csharp
string query = "SELECT id, first_name, created_at FROM customer";
```

Explicit aliases still work and can be mixed with automatic mapping in the same query:

```csharp
string query = "SELECT id, first_name, created_at AS CreatedAt FROM customer";
```

Columns that don't match any property are silently skipped.

## Constructor-Based Entity Creation

With parameterless constructors, non-nullable reference type properties need workarounds like `= string.Empty` to avoid NRT warnings, even though the value always comes from the database:

```csharp
// Parameterless constructor - works, but NRT is awkward
public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;  // has to have a default
    public string? Description { get; set; }
}
```

Constructor-based entities avoid this. Properties can be get-only and non-nullable without workarounds:

```csharp
public class Customer
{
    public Guid Id { get; }
    public string Name { get; }
    public int? Value { get; }

    public Customer(Guid id, string name, int? value)
    {
        Id = id;
        Name = name;
        Value = value;
    }
}
```

Constructor parameters are matched to columns by name (case-insensitive, with snake_case support). When a column is missing from the result set, the parameter receives its default value (`null` for reference types, `0`/`false`/etc. for value types).

Hybrid entities are also supported - use a constructor for required properties and settable properties for optional ones:

```csharp
public class Customer
{
    public Guid Id { get; }
    public string Name { get; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    public Customer(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}
```

Both styles work. The mapper checks whether the entity type has a parameterless constructor and chooses the appropriate strategy. When multiple public constructors exist, the one with the most parameters is chosen.

## Supported Property Types

The following types are supported for entity properties, constructor parameters, and scalar queries:

| Type | PostgreSQL column | Notes |
|---|---|---|
| `string` | `text`, `varchar` | |
| `int`, `long`, `short`, `byte` | `integer`, `bigint`, `smallint` | |
| `float`, `double` | `real`, `double precision` | |
| `decimal` | `numeric`, `decimal` | |
| `bool` | `boolean` | |
| `Guid` | `uuid` | |
| `DateTime` | `timestamp`, `timestamptz` | |
| `DateTimeOffset` | `timestamptz` | Handles Npgsql returning `DateTime` (Kind=Utc) for `timestamptz` columns |
| `DateOnly` | `date` | Read via `GetFieldValue<DateOnly>()` for correct Npgsql handling |
| `TimeOnly` | `time` | Read via `GetFieldValue<TimeOnly>()` for correct Npgsql handling |
| Enum types | Npgsql-mapped enums | Works with both `MapEnum<T>()` and custom type handlers |

All types also work as `Nullable<T>` (e.g. `int?`, `DateOnly?`). NULL database values map to `null` for nullable properties and are skipped for non-nullable ones.

## Design Decisions

- **Compiled expression delegates**  - Property setters and constructors are compiled once via expression trees and cached, replacing `Activator.CreateInstance`, `PropertyInfo.SetValue`, and `ConstructorInfo.Invoke` with near-native speed delegates.
- **Async only**  - No synchronous methods. All database operations should be async.
- **Same method signatures as Dapper**  - Makes migration a `using` statement swap.
- **No external dependencies**  - Only depends on `System.Data.Common` from the framework.
