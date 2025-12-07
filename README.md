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