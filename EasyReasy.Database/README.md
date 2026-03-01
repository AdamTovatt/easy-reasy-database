← [Back to overview](../README.md)

# EasyReasy.Database

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.svg)](https://www.nuget.org/packages/EasyReasy.Database/)

> **Note**: The query examples in this guide use [EasyReasy.Database.Mapping](../EasyReasy.Database.Mapping/README.md) for query execution (e.g., `QueryAsync`, `QuerySingleOrDefaultAsync`). EasyReasy.Database.Mapping is a lightweight Dapper replacement with the same method signatures, but you can use any ADO.NET-compatible query library — this library is database-agnostic.

This file provides an overview of how EasyReasy.Database can be use from two points of view: service developers and repository developers.

If you are thinking about writing a repository class you might want to jump to the section about [creating repositories](#for-repository-developers-creating-repositories). It might also be good to [understand database sessions](#understanding-database-sessions).

If you already have a repository and you are thinking about writing a service that uses it you can read in the section about [using repositories](#for-service-developers-using-repositories). That's the section just below here.

## Installation

```bash
dotnet add package EasyReasy.Database
```

## For Service Developers (Using Repositories)

### Single Query (Most Common)
```csharp
// Repository automatically manages connection lifecycle
CustomerBasic? customer = await _customerRepository.GetBasicAsync(externalId);
```

### Transactions
```csharp
// Multiple operations that must succeed or fail together
await using (IDbSession session = await _customerRepository.CreateSessionWithTransactionAsync())
{
    await _customerRepository.UpdateBasicAsync(externalId, data, session);
    await _anotherRepository.UpdateRelatedDataAsync(externalId, data, session);
    
    await session.CommitAsync();  // Explicit commit required
}  // Auto-rollback if CommitAsync not called
```

### Dependency Injection
```csharp
public class CustomerService
{
    private readonly ICustomerRepository _customerRepository;  // Only need the repository
    
    public CustomerService(ICustomerRepository customerRepository)
    {
        _customerRepository = customerRepository;  // DataSource accessible via repository
    }
}
```

## Understanding Database Sessions

### IDbSession

`IDbSession` represents a database session that wraps a connection and an optional transaction.
It is the core abstraction repositories use when executing queries.

- **Connection**: `DbConnection Connection` – the underlying database connection.
- **Transaction**: `DbTransaction? Transaction` – the active transaction, or `null` if no transaction is active.
- **Commit**: `Task CommitAsync(CancellationToken cancellationToken = default)` – commits the active transaction; throws if there is no transaction.
- **Rollback**: `Task RollbackAsync(CancellationToken cancellationToken = default)` – rolls back the active transaction; throws if there is no transaction.
- **Disposal**: `IDbSession` implements `IAsyncDisposable`. Always dispose sessions (typically with `await using`) so both the connection and transaction are correctly released.

Sessions own their connection and transaction – you should never dispose the `DbConnection` or `DbTransaction` directly, only the `IDbSession`.

### IDbSessionFactory and DbSessionFactory

`IDbSessionFactory` is the abstraction for creating `IDbSession` instances. It is used by `RepositoryBase` so repositories have a consistent way to obtain sessions and so tests can easily provide fake implementations.

**Testing benefit**: Because repositories depend on `IDbSessionFactory` rather than creating sessions directly, you can inject a fake implementation (like `FakeDbSessionFactory`) in unit tests to verify transaction behavior without needing a real database connection.

```csharp
public interface IDbSessionFactory
{
    Task<IDbSession> CreateSessionAsync();
    Task<IDbSession> CreateSessionWithTransactionAsync();
}
```

- **`CreateSessionAsync()`**: Creates a session *without* a transaction. Each command auto-commits when executed.
- **`CreateSessionWithTransactionAsync()`**: Creates a session *with* an active transaction. The caller is responsible for calling `CommitAsync()` or `RollbackAsync()`.

`DbSessionFactory` is the default implementation of `IDbSessionFactory` and uses a `DbDataSource` to open connections:

```csharp
public class DbSessionFactory : IDbSessionFactory
{
    private readonly DbDataSource _dataSource;

    public DbSessionFactory(DbDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IDbSession> CreateSessionAsync()
    {
        DbConnection connection = await _dataSource.OpenConnectionAsync();
        return new DbSession(connection, transaction: null);
    }

    public async Task<IDbSession> CreateSessionWithTransactionAsync()
    {
        DbConnection connection = await _dataSource.OpenConnectionAsync();
        DbTransaction transaction = await connection.BeginTransactionAsync();
        return new DbSession(connection, transaction);
    }
}
```

`DbSession` is the concrete implementation of `IDbSession`. It holds the connection and (optional) transaction and implements the `CommitAsync`, `RollbackAsync`, and `DisposeAsync` behavior described above.

### When to Use Which Session Method

- Use **`CreateSessionAsync()`** when you need a connection but do **not** require transactional guarantees (for example, simple read-only operations).
- Use **`CreateSessionWithTransactionAsync()`** when you need multiple operations to succeed or fail together.

## For Repository Developers (Creating Repositories)

### Repository Inheritance Structure
```csharp
// Base interface - provides session creation
public interface IRepository
{
    DbDataSource DataSource { get; }
    Task<IDbSession> CreateSessionWithTransactionAsync();
}

// Specific repository interface extends IRepository
public interface ICustomerRepository : IRepository
{
    // Methods only - session creation inherited from IRepository
}

// Base class implements IRepository using IDbSessionFactory
public abstract class RepositoryBase : IRepository
{
    public DbDataSource DataSource { get; }
    protected IDbSessionFactory SessionFactory { get; }
    // ... connection management via UseSessionAsync
}
```

### Basic Repository Setup
```csharp
public class CustomerRepository : RepositoryBase, ICustomerRepository
{
    public CustomerRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory) 
        : base(dataSource, sessionFactory)
    {
    }
    
    public async Task<CustomerBasic?> GetBasicAsync(string externalId, IDbSession? session = null)
    {
        return await UseSessionAsync(async (dbSession) =>
        {
            string query = $@"SELECT ... FROM customer WHERE external_id = @{nameof(externalId)}";
            
            return await dbSession.Connection.QuerySingleOrDefaultAsync<CustomerBasic>(
                query,
                new { externalId },
                transaction: dbSession.Transaction);
        }, session);
    }
}
```

### Key Patterns

**Always use `UseSessionAsync`** - it handles connection management and transaction support:
```csharp
protected async Task<T> UseSessionAsync<T>(Func<IDbSession, Task<T>> action, IDbSession? session = null)
```

**Always pass `dbSession.Transaction` to your query library** - ensures query participates in transaction if one exists:
```csharp
await dbSession.Connection.QueryAsync<T>(query, parameters, transaction: dbSession.Transaction);
```

**Repository method signature pattern**:
```csharp
Task<ReturnType> MethodNameAsync(params, IDbSession? session = null)
```
All methods should have an optional parameter of type `IDbSession?` so that an implementation of `IDbSession` can be passed by the caller. This allows callers to control connection and transaction behaviour if they want to but also allows for fallback to a simple one connection per query without any explicit transaction control.

**Consider using `nameof()` for parameters** - instead of hardcoding the names, for example:
```csharp
// Do this:
const string query = $@"SELECT ... FROM customer WHERE external_id = @{nameof(externalId)}";

// Instead of this:
const string query = $@"SELECT ... FROM customer WHERE external_id = @externalId";
```

This allows the parameter name to be renamed automatically from an IDE. It works with `const` too.

### Interface Definition
```csharp
// All repository interfaces must extend IRepository
public interface ICustomerRepository : IRepository
{
    // CreateSessionWithTransactionAsync() inherited from IRepository
    Task<CustomerBasic?> GetBasicAsync(string externalId, IDbSession? session = null);
    // ... more methods
}
```

## Design Principles

- **All repository interfaces extend IRepository** - ensures consistent access to DataSource
- **Repositories never manage transactions** - repositories never create, commit, or rollback transactions; callers control all transaction boundaries
- **Session parameter is always optional** - defaults to single-query connection
- **Explicit commits required** - transactions don't auto-commit
- **Session owns connection** - always dispose sessions (not connections directly). Repositories dispose sessions they create internally; callers dispose sessions they create for transactions.

## Important: Transaction Commit Responsibility

**When you pass a transaction session to a repository method, you MUST explicitly call `CommitAsync()` on the session after your operations complete. The repository will NOT commit the transaction for you.**

This is critical because:
- **Repositories never call `CommitAsync()` or `RollbackAsync()`** - they only execute queries within the provided session
- **If you pass a session with a transaction**, you are responsible for committing it
- **If you don't pass a session** (or pass a session without a transaction), each query auto-commits immediately, so no explicit commit is needed

Example of correct usage:
```csharp
await using (IDbSession session = await _customerRepository.CreateSessionWithTransactionAsync())
{
    await _customerRepository.CreateAsync(...data..., session);
    await _anotherRepository.UpdateRelatedDataAsync(...data..., session);
    
    await session.CommitAsync();  // YOU must call this - repository won't do it
}
```

### What Happens If You Exit Without Committing?

If you exit the `using` statement without calling `CommitAsync()`, the transaction will be **automatically rolled back** when the session is disposed. This means:
- All changes made within the transaction will be **discarded**
- No data will be persisted to the database
- This happens automatically - you don't need to call `RollbackAsync()` explicitly

Example of what NOT to do:
```csharp
await using (IDbSession session = await _customerRepository.CreateSessionWithTransactionAsync())
{
    await _customerRepository.CreateAsync(...data..., session);
    // Oops! Forgot to call CommitAsync() - transaction will be rolled back on disposal
}  // Transaction automatically rolled back here - all changes lost!
```

### What Happens If an Error Occurs?

If an exception occurs during your operations, you have two options:

**Option 1: Let it rollback automatically (simplest)**
```csharp
await using (IDbSession session = await _customerRepository.CreateSessionWithTransactionAsync())
{
    await _customerRepository.CreateAsync(...data..., session);
    await _anotherRepository.UpdateRelatedDataAsync(...data..., session);
    
    await session.CommitAsync();
}  // If an exception occurred above, CommitAsync() was never called, so transaction rolls back automatically
```

**Option 2: Explicitly rollback in a catch block (for clarity)**
```csharp
await using (IDbSession session = await _customerRepository.CreateSessionWithTransactionAsync())
{
    try
    {
        await _customerRepository.CreateAsync(...data..., session);
        await _anotherRepository.UpdateRelatedDataAsync(...data..., session);
        
        await session.CommitAsync();
    }
    catch
    {
        await session.RollbackAsync();  // Explicit rollback (optional - will happen automatically anyway)
        throw;  // Re-throw the exception
    }
}
```

**Important**: If an exception occurs before `CommitAsync()` is called, the transaction will be rolled back automatically when the session is disposed. Explicitly calling `RollbackAsync()` in a catch block is optional but can make your intent clearer.

## Sorting System

The sorting system provides a type-safe way to build ORDER BY clauses for database queries. It uses attributes to mark sortable properties on DTOs and provides helpers to build SQL ORDER BY clauses. This allows you to easily decide what properties on a DTO (columns in a table) you want to be able to sort on in a type-safe manner and without having to write any manual sorting code.

### For Model/DTO Developers (Marking Properties as Sortable)

#### Basic Usage
Mark properties that should be sortable with the `[Sortable]` attribute:

```csharp
public class CustomerWithCoverCounts
{
    [Sortable]
    public int Id { get; set; }

    [Sortable]
    public string FirstName { get; set; }

    [Sortable]
    public string LastName { get; set; }
}
```

#### Custom SQL Column Names
If your property name doesn't match the database column name, specify it explicitly:

```csharp
[Sortable("total_cover_count")]
public int TotalCoverCount { get; set; }
```

#### Default Sort Column
Mark one property as the default sort column (used when no sort is specified):

```csharp
[Sortable(isDefault: true)]
public DateTime SignUpTime { get; set; }
```

#### Combined Options
You can combine custom column name and default sort:

```csharp
[Sortable("sign_up_time", isDefault: true)]
public DateTime SignUpTime { get; set; }
```

#### Auto-Generated Column Names
If no custom column name is specified, the property name is automatically converted to snake_case:
- `FirstName` → `first_name`
- `ActiveCoverCount` → `active_cover_count`
- `ExternalID` → `external_id`

### For Repository Developers (Building ORDER BY Clauses)

#### Using OrderBy Helper (Recommended)
The `OrderBy.Create<TDto>()` method handles default sorting and validation:

```csharp
public async Task<List<CustomerWithCoverCounts>> GetAllWithCoverCountsAsync(
    SortColumn? sortColumn = null,
    SortOrder sortOrder = SortOrder.Descending,
    IDbSession? session = null)
{
    string query = $@"
        SELECT ...
        FROM customer c
        {OrderBy.Create<CustomerWithCoverCounts>(sortColumn, sortOrder)}
        OFFSET @offset
        LIMIT @perPage";
    
    // ... execute query
}
```

**Benefits:**
- Automatically uses default sort column if `sortColumn` is null
- Returns empty string if no sorting is available
- Validates that the sort column is valid (via `SortColumn.Create`)

#### Creating SortColumn from Property Name
Validate and create a `SortColumn` from a property name string:

```csharp
// Validates property exists and is marked as sortable
SortColumn sortColumn = SortColumn.Create<CustomerWithCoverCounts>("FirstName");
// sortColumn.SqlColumnName will be "first_name"
```

**Throws `ArgumentException` if:**
- Property doesn't exist
- Property isn't marked with `[Sortable]`

#### Using SortableFieldHelper Directly
For more control, use `SortableFieldHelper` methods:

```csharp
// Get SQL column name for a property
string sqlColumn = SortableFieldHelper.GetSqlColumnName<CustomerWithCoverCounts>("FirstName");
// Returns "first_name" (or custom column name if specified)

// Get default sort column
string? defaultColumn = SortableFieldHelper.GetDefaultSortColumn<CustomerWithCoverCounts>();
// Returns "sign_up_time" if SignUpTime is marked as default

// Build ORDER BY clause manually
string orderBy = SortableFieldHelper.BuildOrderByClause("first_name", SortOrder.Ascending);
// Returns "ORDER BY first_name ASC"
```

#### Getting All Sortable Fields
Get a list of all sortable property names (useful for API documentation):

```csharp
List<string> sortableFields = SortableFieldHelper.GetSortableFields<CustomerWithCoverCounts>();
// Returns: ["Id", "FirstName", "LastName", "Email", "SignUpTime", ...]
```

### For Service/API Developers (Using Sort Parameters)

#### Accepting Sort Parameters
Repository methods should accept `SortColumn?` and `SortOrder` parameters:

```csharp
public async Task<List<CustomerWithCoverCounts>> GetAllWithCoverCountsAsync(
    int page,
    int perPage,
    SortColumn? sortColumn = null,
    SortOrder sortOrder = SortOrder.Descending,
    IDbSession? session = null)
```

#### Creating SortColumn from User Input
When receiving sort requests from API endpoints, validate and create `SortColumn`:

```csharp
// In your service/controller
string? sortField = request.SortField; // e.g., "FirstName" or "firstName"

SortColumn? sortColumn = null;
if (!string.IsNullOrEmpty(sortField))
{
    try
    {
        sortColumn = SortColumn.Create<CustomerWithCoverCounts>(sortField);
    }
    catch (ArgumentException)
    {
        // Handle invalid sort field
        throw new BadRequestException($"Invalid sort field: {sortField}");
    }
}

SortOrder sortOrder = request.SortOrder == "asc" 
    ? SortOrder.Ascending 
    : SortOrder.Descending;

List<CustomerWithCoverCounts> results = await _customerRepository
    .GetAllWithCoverCountsAsync(page, perPage, sortColumn, sortOrder);
```

#### Getting Available Sort Fields
Expose available sort fields to API consumers:

```csharp
// In your API endpoint
[HttpGet("sortable-fields")]
public IActionResult GetSortableFields()
{
    List<string> fields = SortableFieldHelper.GetSortableFields<CustomerWithCoverCounts>();
    return Ok(fields);
}
```

### Sorting Design Principles

- **All sortable properties must be marked with `[Sortable]`** - ensures explicit control over what can be sorted
- **Property names are case-insensitive** - `"FirstName"` and `"firstName"` both work
- **Auto snake_case conversion** - property names automatically convert to database column names
- **Custom column names override auto-conversion** - use when property name doesn't match database
- **Default sort is optional** - if no default is specified and no sort column provided, no ORDER BY is added
- **SortColumn validates at creation time** - invalid properties throw exceptions immediately
- **OrderBy handles null gracefully** - returns empty string if no sorting is possible
