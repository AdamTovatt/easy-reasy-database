# EasyReasy Database System Overview

[![Tests](https://github.com/AdamTovatt/easy-reasy-database/actions/workflows/build.yml/badge.svg)](https://github.com/AdamTovatt/easy-reasy-database/actions/workflows/build.yml)

The EasyReasy Database system simplifies database integration and testing. It provides a standardized way to write repositories with automatic connection and transaction management, and includes testing utilities that make integration tests performant by running them in transactions that are automatically rolled back.

## Getting Started
Click the name of the library you want to read more about in the table below to get started.

## Projects

| Project | NuGet | Description |
|---------|-------|-------------|
| [EasyReasy.Database](EasyReasy.Database/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.svg)](https://www.nuget.org/packages/EasyReasy.Database/) | Core database library providing repository base classes, session management, and database abstractions for building data access layers. |
| [EasyReasy.Database.Npgsql](EasyReasy.Database.Npgsql/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Npgsql/) | PostgreSQL-specific implementation of `IDataSourceFactory` for creating Npgsql data sources with optional enum mapping support. |
| [EasyReasy.Database.Sqlite](EasyReasy.Database.Sqlite/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Sqlite.svg)](https://www.nuget.org/packages/EasyReasy.Database.Sqlite/) | SQLite-specific implementation of `IDataSourceFactory` for creating SQLite data sources. |
| [EasyReasy.Database.Testing](EasyReasy.Database.Testing/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Testing.svg)](https://www.nuget.org/packages/EasyReasy.Database.Testing/) | Database-agnostic testing utilities: fake database sessions for unit tests, test database management for integration tests with automatic transaction rollback, per-checkout test database naming so concurrent git worktrees never share a database, and repository-root lookup. |
| [EasyReasy.Database.Testing.Npgsql](EasyReasy.Database.Testing.Npgsql/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Testing.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Testing.Npgsql/) | PostgreSQL-specific testing utilities: per-checkout test database provisioning with ownership markers, a cluster-wide advisory-lock mutex, and idempotent shared-role bootstrap, so concurrent git worktrees can run test suites against one cluster without coordination; plus a backend-blocking observer, so a concurrency test can prove it reached the interleaving it asserts about. |
| [EasyReasy.Database.Testing.PruneTool](EasyReasy.Database.Testing.PruneTool/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Testing.PruneTool.svg)](https://www.nuget.org/packages/EasyReasy.Database.Testing.PruneTool/) | `prune-test-databases` dotnet tool that reclaims the per-checkout test databases (and optional scratch directories) of checkouts that no longer exist, using the ownership markers `EasyReasy.Database.Testing.Npgsql` stamps. |
| [EasyReasy.Database.Mapping](EasyReasy.Database.Mapping/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Mapping.svg)](https://www.nuget.org/packages/EasyReasy.Database.Mapping/) | Lightweight database mapping library that maps `DbDataReader` rows to CLR objects with snake_case to PascalCase column mapping, constructor-based entity creation, custom type handlers, and enum support. |
| [EasyReasy.Database.Mapping.Npgsql](EasyReasy.Database.Mapping.Npgsql/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Mapping.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Mapping.Npgsql/) | Npgsql-specific enum handler for `EasyReasy.Database.Mapping`. Automatically sets `NpgsqlParameter.DataTypeName`, eliminating the need for `::pg_type` casts in SQL queries. |
| [EasyReasy.Database.Logging](EasyReasy.Database.Logging/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Logging.svg)](https://www.nuget.org/packages/EasyReasy.Database.Logging/) | Database-agnostic operational logging built on Serilog. Persists structured log events to a SQL table via a batched best-effort sink, with a paginated read surface and an in-process live feed. |
| [EasyReasy.Database.Logging.Npgsql](EasyReasy.Database.Logging.Npgsql/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Logging.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Logging.Npgsql/) | PostgreSQL fast path for `EasyReasy.Database.Logging`: a binary `COPY` sink for high-throughput bulk insert, monthly range-partition retention, and the partitioned table DDL. |
| [EasyReasy.Database.Logging.AspNetCore](EasyReasy.Database.Logging.AspNetCore/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Logging.AspNetCore.svg)](https://www.nuget.org/packages/EasyReasy.Database.Logging.AspNetCore/) | ASP.NET Core integration for `EasyReasy.Database.Logging`: an `HttpContext` enricher attaching user id and client IP, plus opt-in admin endpoints (paginated read + Server-Sent Events live feed). |

## Publishing

Each package is published to NuGet by pushing a git tag of the form `<package>-v<version>`. The [`Publish NuGet`](.github/workflows/publish.yml) workflow picks up the tag, builds the matching project in `Release` with the version baked in (`-p:Version=<version>`), runs the matching test project (excluding `IntegrationTests` and `PerformanceTest`), packs, and pushes to nuget.org using the `NUGET_API_KEY` repo secret with `--skip-duplicate`.

| Package | Tag prefix | Project | Test project |
|---------|-----------|---------|--------------|
| EasyReasy.Database | `core` | `EasyReasy.Database` | `EasyReasy.Database.Tests` |
| EasyReasy.Database.Sqlite | `sqlite` | `EasyReasy.Database.Sqlite` | `EasyReasy.Database.Tests` |
| EasyReasy.Database.Testing | `testing` | `EasyReasy.Database.Testing` | `EasyReasy.Database.Tests` |
| EasyReasy.Database.Npgsql | `npgsql` | `EasyReasy.Database.Npgsql` | `EasyReasy.Database.Tests` |
| EasyReasy.Database.Testing.Npgsql | `testing-npgsql` | `EasyReasy.Database.Testing.Npgsql` | `EasyReasy.Database.Testing.Npgsql.Tests` |
| EasyReasy.Database.Testing.PruneTool | `testing-prunetool` | `EasyReasy.Database.Testing.PruneTool` | `EasyReasy.Database.Testing.Npgsql.Tests` |
| EasyReasy.Database.Mapping | `mapping` | `EasyReasy.Database.Mapping` | `EasyReasy.Database.Mapping.Tests` |
| EasyReasy.Database.Mapping.Npgsql | `mapping-npgsql` | `EasyReasy.Database.Mapping.Npgsql` | `EasyReasy.Database.Mapping.Npgsql.Tests` |
| EasyReasy.Database.Logging | `logging` | `EasyReasy.Database.Logging` | `EasyReasy.Database.Logging.Tests` |
| EasyReasy.Database.Logging.Npgsql | `logging-npgsql` | `EasyReasy.Database.Logging.Npgsql` | `EasyReasy.Database.Logging.Npgsql.Tests` |
| EasyReasy.Database.Logging.AspNetCore | `logging-aspnetcore` | `EasyReasy.Database.Logging.AspNetCore` | `EasyReasy.Database.Logging.AspNetCore.Tests` |

Example — publish `EasyReasy.Database.Mapping` 1.2.0:

```bash
git tag mapping-v1.2.0
git push origin mapping-v1.2.0
```

### Release order

A package's dependencies must already be on nuget.org when it is pushed, so tag bottom-up within a dependency chain and let each push finish before the next:

```
EasyReasy.Database                  (core; no dependencies in this repository)
  └─ EasyReasy.Database.Testing.Npgsql
       └─ EasyReasy.Database.Testing.PruneTool
```

`EasyReasy.Database.Testing`, `.Npgsql`, `.Sqlite`, `.Mapping` and `.Logging` all sit directly on core and are independent of each other.

The tag version overrides the csproj `VersionPrefix` at build time, so the source-controlled version mainly matters for local `dotnet pack` runs. Bumping it alongside the change being shipped is still recommended so `git blame` on the csproj tells the same story as the tag.