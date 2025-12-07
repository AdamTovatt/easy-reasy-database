# EasyReasy Database System Overview

The Easy Reasy Database system is created to make it easier to write database integrations as well as unit tests and database integration tests related to that. It provides a simplified and standardized way to write repositories and tests for them that are performant since they run the test code inside transactions that can just be rolled back.

## Projects

| Project | Description |
|---------|-------------|
| [EasyReasy.Database](EasyReasy.Database/README.md) | Core database library providing repository base classes, session management, and database abstractions for building data access layers. |
| [EasyReasy.Database.Npgsql](EasyReasy.Database.Npgsql/README.md) | PostgreSQL-specific implementation of `IDataSourceFactory` for creating Npgsql data sources with optional enum mapping support. |
| [EasyReasy.Database.Sqlite](EasyReasy.Database.Sqlite/README.md) | SQLite-specific implementation of `IDataSourceFactory` for creating SQLite data sources. |
| [EasyReasy.Database.Testing](EasyReasy.Database.Testing/README.md) | Testing utilities including fake database sessions for unit tests and test database management for integration tests with automatic transaction rollback. |