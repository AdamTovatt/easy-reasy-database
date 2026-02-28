← [Back to overview](../README.md)

# EasyReasy.Database.Mapping.Npgsql

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Mapping.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Mapping.Npgsql/)

Npgsql-specific enum handler for [EasyReasy.Database.Mapping](../EasyReasy.Database.Mapping/README.md). Automatically sets `NpgsqlParameter.DataTypeName` on enum parameters, eliminating the need for `::pg_type` casts in SQL queries.

## Installation

```bash
dotnet add package EasyReasy.Database.Mapping.Npgsql
```

## Usage

With the base `DbNameEnumHandler<T>`, you need explicit casts in your SQL:

```csharp
// Without this package — requires ::mapping_test_status cast
TypeHandlerRegistry.AddTypeHandler(new DbNameEnumHandler<TestStatus>());

await connection.ExecuteAsync(
    "INSERT INTO users (name, status) VALUES (@name, @status::user_status)",
    new { name = "Alice", status = UserStatus.Active });
```

With `NpgsqlDbNameEnumHandler<T>`, the cast is handled automatically:

```csharp
// With this package — no cast needed
TypeHandlerRegistry.AddTypeHandler(new NpgsqlDbNameEnumHandler<UserStatus>("user_status"));

await connection.ExecuteAsync(
    "INSERT INTO users (name, status) VALUES (@name, @status)",
    new { name = "Alice", status = UserStatus.Active });
```
