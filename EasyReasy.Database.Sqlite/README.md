← [Back to overview](../README.md)

# EasyReasy.Database.Sqlite

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Sqlite.svg)](https://www.nuget.org/packages/EasyReasy.Database.Sqlite/)

SQLite-specific implementation of `IDataSourceFactory` for creating SQLite data sources.

## Installation

```bash
dotnet add package EasyReasy.Database.Sqlite
```

## SqliteDataSourceFactory

`SqliteDataSourceFactory` implements `IDataSourceFactory` and creates SQLite data sources from connection strings.

### Usage

```csharp
IDataSourceFactory factory = new SqliteDataSourceFactory();
DbDataSource dataSource = factory.CreateDataSource(connectionString);
```

The factory creates a SQLite data source using the Microsoft.Data.Sqlite provider.

