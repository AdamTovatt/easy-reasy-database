# EasyReasy.Database.Mapping

A lightweight SQL-to-object mapping library that provides extension methods on `DbConnection` for executing queries and deserializing results. Designed as a focused replacement for Dapper that correctly handles custom type handlers for all types, including enums.

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

All methods are async extension methods on `DbConnection`. Parameters are passed as anonymous objects.

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

### Defining a Handler

```csharp
public class CustomerStatusHandler : TypeHandler<CustomerStatus>
{
    public override void SetValue(IDbDataParameter parameter, CustomerStatus value)
    {
        parameter.Value = value switch
        {
            CustomerStatus.Active => "active",
            CustomerStatus.Inactive => "inactive",
            _ => throw new ArgumentOutOfRangeException()
        };
        parameter.DbType = DbType.String;
    }

    public override CustomerStatus Parse(object value)
    {
        return value.ToString() switch
        {
            "active" => CustomerStatus.Active,
            "inactive" => CustomerStatus.Inactive,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
```

### Registering a Handler

```csharp
TypeHandlerRegistry.AddTypeHandler(new CustomerStatusHandler());
```

Once registered, the handler is used automatically for both parameter binding (writes) and result deserialization (reads).

## Column Mapping

Result columns are mapped to entity properties by name (case-insensitive). Use SQL column aliases to match property names:

```csharp
// Maps columns to PascalCase properties
string query = @"
    SELECT
        id AS Id,
        first_name AS FirstName,
        created_at AS CreatedAt
    FROM customer";
```

## Design Decisions

- **Reflection, not IL emit** &mdash; Uses reflection for parameter binding and result deserialization. The performance difference is negligible when database I/O dominates.
- **Async only** &mdash; No synchronous methods. All database operations should be async.
- **Same method signatures as Dapper** &mdash; Makes migration a `using` statement swap.
- **No external dependencies** &mdash; Only depends on `System.Data.Common` from the framework.
