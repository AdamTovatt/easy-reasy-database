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
| [EasyReasy.Database.Testing](EasyReasy.Database.Testing/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Testing.svg)](https://www.nuget.org/packages/EasyReasy.Database.Testing/) | Testing utilities including fake database sessions for unit tests and test database management for integration tests with automatic transaction rollback. |
| [EasyReasy.Database.Mapping](EasyReasy.Database.Mapping/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Mapping.svg)](https://www.nuget.org/packages/EasyReasy.Database.Mapping/) | Lightweight database mapping library that maps `DbDataReader` rows to CLR objects with snake_case to PascalCase column mapping, constructor-based entity creation, custom type handlers, and enum support. |
| [EasyReasy.Database.Mapping.Npgsql](EasyReasy.Database.Mapping.Npgsql/README.md) | [![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Mapping.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Mapping.Npgsql/) | Npgsql-specific enum handler for `EasyReasy.Database.Mapping`. Automatically sets `NpgsqlParameter.DataTypeName`, eliminating the need for `::pg_type` casts in SQL queries. |

## Publishing

Each package is published to NuGet by pushing a git tag of the form `<package>-v<version>`. The [`Publish NuGet`](.github/workflows/publish.yml) workflow picks up the tag, builds the matching project in `Release` with the version baked in (`-p:Version=<version>`), runs the matching test project (excluding `IntegrationTests` and `PerformanceTest`), packs, and pushes to nuget.org using the `NUGET_API_KEY` repo secret with `--skip-duplicate`.

| Package | Tag prefix | Project | Test project |
|---------|-----------|---------|--------------|
| EasyReasy.Database | `core` | `EasyReasy.Database` | `EasyReasy.Database.Tests` |
| EasyReasy.Database.Sqlite | `sqlite` | `EasyReasy.Database.Sqlite` | `EasyReasy.Database.Tests` |
| EasyReasy.Database.Testing | `testing` | `EasyReasy.Database.Testing` | `EasyReasy.Database.Tests` |
| EasyReasy.Database.Npgsql | `npgsql` | `EasyReasy.Database.Npgsql` | `EasyReasy.Database.Tests` |
| EasyReasy.Database.Mapping | `mapping` | `EasyReasy.Database.Mapping` | `EasyReasy.Database.Mapping.Tests` |
| EasyReasy.Database.Mapping.Npgsql | `mapping-npgsql` | `EasyReasy.Database.Mapping.Npgsql` | `EasyReasy.Database.Mapping.Npgsql.Tests` |

Example — publish `EasyReasy.Database.Mapping` 1.2.0:

```bash
git tag mapping-v1.2.0
git push origin mapping-v1.2.0
```

The tag version overrides the csproj `VersionPrefix` at build time, so the source-controlled version mainly matters for local `dotnet pack` runs. Bumping it alongside the change being shipped is still recommended so `git blame` on the csproj tells the same story as the tag.