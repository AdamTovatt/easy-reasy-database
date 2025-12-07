# EasyReasy Database System Overview

The EasyReasy Database system simplifies database integration and testing. It provides a standardized way to write repositories with automatic connection and transaction management, and includes testing utilities that make integration tests performant by running them in transactions that are automatically rolled back.

## Projects

| Project | Description |
|---------|-------------|
| [EasyReasy.Database](EasyReasy.Database/README.md) | Core database library providing repository base classes, session management, and database abstractions for building data access layers. |
| [EasyReasy.Database.Npgsql](EasyReasy.Database.Npgsql/README.md) | PostgreSQL-specific implementation of `IDataSourceFactory` for creating Npgsql data sources with optional enum mapping support. |
| [EasyReasy.Database.Sqlite](EasyReasy.Database.Sqlite/README.md) | SQLite-specific implementation of `IDataSourceFactory` for creating SQLite data sources. |
| [EasyReasy.Database.Testing](EasyReasy.Database.Testing/README.md) | Testing utilities including fake database sessions for unit tests and test database management for integration tests with automatic transaction rollback. |