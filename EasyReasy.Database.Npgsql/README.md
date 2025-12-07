← [Back to overview](../README.md)

# EasyReasy.Database.Npgsql

PostgreSQL-specific implementation of `IDataSourceFactory` for creating Npgsql data sources.

## NpgsqlDataSourceFactory

`NpgsqlDataSourceFactory` implements `IDataSourceFactory` and creates configured Npgsql data sources from connection strings.

### Basic Usage

```csharp
IDataSourceFactory factory = new NpgsqlDataSourceFactory();
DbDataSource dataSource = factory.CreateDataSource(connectionString);
```

### With Enum Mappings

For PostgreSQL projects that use enum types, you can configure enum mappings:

```csharp
IDataSourceFactory factory = new NpgsqlDataSourceFactory(builder =>
{
    builder.MapEnum<MyEnumType>();
});
DbDataSource dataSource = factory.CreateDataSource(connectionString);
```

The factory applies the configured builder action (such as enum mappings) when creating the data source.
You can use the builder action for whatever you want that you need to do as setup on the `NpgsqlDataSourceBuilder` that will eventually build the `NpgsqlDataSource` and return it as a `DbDataSource`.

