← [Back to overview](../README.md)

# EasyReasy.Database.Logging.Npgsql

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Logging.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Logging.Npgsql/)

The **PostgreSQL fast path** for [EasyReasy.Database.Logging](../EasyReasy.Database.Logging/README.md). Adds:

- a **binary `COPY FROM STDIN` sink** for high-throughput bulk insert, binding the `properties` column as native `jsonb`;
- a **monthly range-partition retention** background service (create current + 2 future months, drop partitions older than the retention window);
- the **partitioned table DDL** (`OperationalLogSchema.CreateTableSql`).

## Installation

```bash
dotnet add package EasyReasy.Database.Logging.Npgsql
```

## Usage

Add it after the core registration — it swaps the agnostic `INSERT` sink for the `COPY` sink:

```csharp
builder.Services.AddOperationalLogging(options => options.TableName = "operational_log");
builder.Services.AddNpgsqlOperationalLogging(npgsql =>
{
    npgsql.Retention = TimeSpan.FromDays(90);   // default
    npgsql.MaintainPartitions = true;           // default
});
builder.Host.UseOperationalLogging();
```

The registered `DbDataSource` must be an `NpgsqlDataSource` (as provided by `EasyReasy.Database.Npgsql`).

## Creating the table

Run the DDL through your own migration (e.g. dbup) — the library does not own your migration sequence:

```csharp
string ddl = OperationalLogSchema.CreateTableSql("operational_log");
// add `ddl` as a migration script
```

The DDL creates the range-partitioned parent, the indexes, the current + next two monthly partitions, and a `DEFAULT` partition so writes never fail before the maintenance service first runs. The table name must be an **unqualified** identifier (it is also used to derive partition and index names).
