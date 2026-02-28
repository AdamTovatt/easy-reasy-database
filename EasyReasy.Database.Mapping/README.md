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

```csharp
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

For non-enum types, subclass `TypeHandler<T>` to control how values are written to and read from the database. For example, a handler that stores objects as JSON in a text column:

```csharp
public class JsonTypeHandler<T> : TypeHandler<T>
{
    public override void SetValue(IDbDataParameter parameter, T value)
    {
        parameter.Value = JsonSerializer.Serialize(value);
        parameter.DbType = DbType.String;
    }

    public override T? Parse(object value)
    {
        return JsonSerializer.Deserialize<T>(value.ToString()!);
    }
}
```

```csharp
TypeHandlerRegistry.AddTypeHandler(new JsonTypeHandler<Address>());
```

Once registered, handlers are used automatically for both parameter binding (writes) and result deserialization (reads).

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

## Design Decisions

- **Compiled expression delegates**  - Property setters and constructors are compiled once via expression trees and cached, replacing `Activator.CreateInstance`, `PropertyInfo.SetValue`, and `ConstructorInfo.Invoke` with near-native speed delegates.
- **Async only**  - No synchronous methods. All database operations should be async.
- **Same method signatures as Dapper**  - Makes migration a `using` statement swap.
- **No external dependencies**  - Only depends on `System.Data.Common` from the framework.
