← [Back to overview](../README.md)

# EasyReasy.Database.Logging

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Logging.svg)](https://www.nuget.org/packages/EasyReasy.Database.Logging/)

Database-agnostic **operational logging** for .NET, built on [Serilog](https://serilog.net/). It persists structured application / observability log events to a SQL table through a batched, best-effort sink, and gives you a paginated read surface plus an in-process live feed for an admin log viewer.

Your existing `ILogger<T>` call sites are unchanged — Serilog is installed as the logging provider and sits behind them.

> This is the **provider-agnostic core**. It writes via a portable multi-row `INSERT` (through [EasyReasy.Database.Mapping](../EasyReasy.Database.Mapping/README.md)), so it runs on any provider. For PostgreSQL, also add **EasyReasy.Database.Logging.Npgsql** for the faster binary-`COPY` sink and monthly-partition retention. For request-scoped user/IP enrichment and ready-made admin HTTP endpoints, add **EasyReasy.Database.Logging.AspNetCore**.

## Installation

```bash
dotnet add package EasyReasy.Database.Logging
```

## Quick start

```csharp
// A DbDataSource must already be registered (e.g. via your EasyReasy.Database setup).
builder.Services.AddOperationalLogging(options =>
{
    options.TableName = "operational_log";          // default
    options.MinimumLevelOverrides["Microsoft.AspNetCore"] = LogEventLevel.Warning;
});

builder.Host.UseOperationalLogging();               // installs Serilog as the ILogger provider
```

That's it — every `_logger.LogInformation(...)` now batches to the table, with the trace id attached as `CorrelationId` and a parallel console sink as a database-outage safety net.

## Reading logs back

Inject `IOperationalLogReadRepository` for a paginated, filterable read:

```csharp
PagedResult<OperationalLogEntry> page = await readRepository.GetAsync(
    new OperationalLogFilters { Level = "Error" },
    page: 1,
    perPage: 50);
```

## Live feed

The sink publishes every persisted row to `IOperationalLogBroadcaster` (in-process, in-memory). Subscribe to stream rows to a live admin view (the AspNetCore package ships a ready-made SSE endpoint over this).

## The table

The core does not own your schema — create the table in your own migration. Columns: `id`, `created_at`, `level`, `source_context`, `message`, `message_template`, `exception`, `correlation_id`, `user_id`, `properties`. The portable sink binds `properties` as JSON-in-text; on PostgreSQL with a `jsonb` column use the Npgsql package, whose `COPY` sink binds `jsonb` natively (it also ships the partitioned DDL).

## What's logged where

Routing is by Serilog convention — everything flows to the operational table. This package intentionally has **no** audit / compliance semantics; it's for operational observability only.
