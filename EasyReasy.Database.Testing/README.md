← [Back to overview](../README.md)

# EasyReasy.Database.Testing

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Testing.svg)](https://www.nuget.org/packages/EasyReasy.Database.Testing/)

Utilities for testing code that uses the EasyReasy.Database library.

## Installation

```bash
dotnet add package EasyReasy.Database.Testing
```

> **Version note:** 3.0.0 requires [EasyReasy.Database](../EasyReasy.Database/README.md) 2.0.0, and core 2.0.0 works only with Testing 3.0.0 or later. Upgrade both together. In 3.0.0:
> - Testing 1.x and 2.x declare a core dependency that NuGet also satisfies with core 2.0.0, so the mismatch installs without a warning and then fails at runtime with a missing-method or type-load error in `FakeDbSessionFactory` and `TestDatabaseManager`.
> - `FakeDbSessionFactory.CreateSessionWithTransactionAsync()` and `TestDatabaseManager.CreateTransactionSessionAsync()` return `IDbTransactionSession`.
> - `FakeDbSession.Transaction` returns a [`FakeDbTransaction`](#fakedbtransaction) instead of `null`. The factory returns its one shared session from both of its methods, so a session from `CreateSessionAsync()` has that non-null transaction too.
> - Like 2.0.0, 3.0.0 targets `net10.0`, so a .NET 8 project that uses this package stays on Testing 1.0.1 and core 1.x.
>
> 2.0.0 targets `net10.0` (1.x targeted `net8.0`). The framework bump is the only breaking change — every API that shipped in 1.x is unchanged. Projects still on .NET 8 should stay on 1.0.1; everything added in 2.0.0 (per-checkout database naming, repository-root lookup) is net10-only, matching the newer packages in this repository.

## For Service Tests (Unit Tests)

Use `FakeDbSession` and `FakeDbSessionFactory` when unit testing services. Mock repositories and verify transaction behavior without a real database.

> **Note**: The examples below use Moq for mocking, but the concepts apply to any mocking framework as well as if creating manual mock classes.

### Basic Setup

```csharp
public class MyServiceTests
{
    private Mock<IMyRepository> MockRepository { get; set; } = null!;
    private FakeDbSessionFactory FakeSessionFactory { get; set; } = null!;
    private MyService Service { get; set; } = null!;

    public void SetUp()
    {
        MockRepository = new Mock<IMyRepository>();
        FakeSessionFactory = new FakeDbSessionFactory();

        // Setup repository to return fake session
        MockRepository
            .Setup(r => r.CreateSessionWithTransactionAsync())
            .ReturnsAsync(FakeSessionFactory.Session);

        Service = new MyService(MockRepository.Object);
    }
}
```

### Verifying Transaction Behavior

```csharp
[Test]
public async Task MyMethod_WhenValid_CommitsTransaction()
{
    // ... arrange and act ...

    Assert.IsTrue(FakeSessionFactory.Session.WasCommitted, "Should commit transaction");
}
```

### Verifying Repository Calls

```csharp
// Using Moq
MockRepository.Verify(
    r => r.SomeMethod(expectedParam, FakeSessionFactory.Session),
    Times.Once,
    "Should pass session to repository");
```

**Key Properties:**
- `FakeDbSession.WasCommitted` - Tracks if `CommitAsync()` was called
- `FakeDbSession.WasRolledBack` - Tracks if `RollbackAsync()` was called
- `FakeDbSession.WasDisposed` - Tracks if session was disposed
- `FakeDbSession.Transaction` - One `FakeDbTransaction` per session, never `null`, the same instance whether the session is read as an `IDbTransactionSession` or an `IDbSession`
- `FakeDbSessionFactory.CreateSessionCallCount` - Number of times `CreateSessionAsync()` was called
- `FakeDbSessionFactory.CreateSessionWithTransactionCallCount` - Number of times `CreateSessionWithTransactionAsync()` was called

`FakeDbSession` implements `IDbTransactionSession`, so it can be returned from `CreateSessionWithTransactionAsync()` setups and passed to repository methods that take a required `IDbTransactionSession`.

### FakeDbTransaction

`FakeDbTransaction` is a do-nothing `DbTransaction`: commit and rollback do nothing, `Connection` is `null`, and `IsolationLevel` is `Unspecified`. It gives your own test sessions and mocks a non-null transaction to return without a database. Track commits and rollbacks on the session, as `FakeDbSession` does, not on the transaction.

### Mocking IDbTransactionSession

Prefer `FakeDbSession` over a mock. If you do mock `IDbTransactionSession`, set up `Transaction` twice. `IDbTransactionSession.Transaction` hides `IDbSession.Transaction`, so they are two separate interface members, and a mock answers each one separately. Code that reads the session as an `IDbSession`, such as a shared guard that checks `session.Transaction != null`, reads the second one:

```csharp
FakeDbTransaction transaction = new FakeDbTransaction();
Mock<IDbTransactionSession> session = new Mock<IDbTransactionSession>();

session.Setup(s => s.Transaction).Returns(transaction);                  // read as IDbTransactionSession
session.As<IDbSession>().Setup(s => s.Transaction).Returns(transaction); // read as IDbSession
```

With only the first setup, `((IDbSession)session.Object).Transaction` returns `null`. A class with one public `Transaction` property, like `FakeDbSession` or your own test session, answers both members from that one property.

## For Repository Tests (Integration Tests)

Use `TestDatabaseManager` for integration tests that require a real database. Each test runs in a transaction that is automatically rolled back.

### Test Run Setup

Before running tests, configure `TestDatabaseManager` once per test assembly:

```csharp
// In your test assembly setup (e.g., AssemblyInitialize, SetUpFixture, etc.)
IDataSourceFactory dataSourceFactory = new SomeDataSourceFactory();
Func<string> connectionStringProvider = () => "your-connection-string";

TestDatabaseManager manager = new TestDatabaseManager(dataSourceFactory, connectionStringProvider);

// Optionally, provide database reset/setup logic
ITestDatabaseSetup setup = new SomeTestDatabaseSetup(); // Your implementation
await manager.EnsureCleanDatabaseSetupAsync(setup);
```

### Repository Test Pattern

```csharp
public class MyRepositoryTests
{
    private TestDatabaseManager TestDatabaseManager { get; set; } = null!;
    private MyRepository Repository { get; set; } = null!;

    public void SetUp()
    {
        // Get the configured TestDatabaseManager instance
        // (configured in assembly setup)
        TestDatabaseManager = GetTestDatabaseManager();

        IDbSessionFactory sessionFactory = new DbSessionFactory(TestDatabaseManager.DataSource);
        Repository = new MyRepository(TestDatabaseManager.DataSource, sessionFactory);
    }

    [Test]
    public async Task MyMethod_WhenValid_ReturnsExpected()
    {
        // Create a transaction session for this test
        await using (IDbTransactionSession session = await TestDatabaseManager.CreateTransactionSessionAsync())
        {
            // All changes are automatically rolled back after this test
            int id = await Repository.CreateAsync(..., session);
            
            Assert.IsTrue(id > 0);
        }
        // Transaction automatically rolled back on disposal
    }
}
```

### Key Principles

1. **Transaction per test**: Each test creates its own transaction via `CreateTransactionSessionAsync()`
2. **Never commit**: Transactions are intentionally never committed - they roll back automatically on disposal
3. **Isolation**: Tests don't interfere with each other since each runs in its own transaction
4. **Database reset**: Happens once per test run (not per test) via `EnsureCleanDatabaseSetupAsync()`

### Database Reset and Setup

Implement `ITestDatabaseSetup` to provide database reset and setup logic:

```csharp
public class MyTestDatabaseSetup : ITestDatabaseSetup
{
    public async Task ResetDatabaseAsync(DbConnection connection)
    {
        // Reset database (e.g., drop/recreate schema)
        await connection.ExecuteAsync("DROP SCHEMA public CASCADE");
        await connection.ExecuteAsync("CREATE SCHEMA public");
        await connection.ExecuteAsync("GRANT ALL ON SCHEMA public TO postgres");
        await connection.ExecuteAsync("GRANT ALL ON SCHEMA public TO public");
    }

    public void SetupDatabase()
    {
        // Run migrations, seed data, etc.
        // This runs after ResetDatabaseAsync and DataSource recreation
    }
}
```

**Important**: After `ResetDatabaseAsync()` completes, `TestDatabaseManager` automatically recreates the `DataSource`. This is necessary because schema resets destroy custom types (including enums), and they get new OIDs when recreated. The `DataSource` must be recreated to pick up the new OIDs.

### Usage Pattern

**Always pass the session to repository methods:**
```csharp
CustomerBasic? result = await Repository.GetBasicAsync(externalId, session);
int id = await Repository.CreateAsync(..., session: session);
```

**Use `DataSource` and `SessionFactory` when creating repositories:**
```csharp
IDbSessionFactory sessionFactory = new DbSessionFactory(TestDatabaseManager.DataSource);
Repository = new CustomerRepository(TestDatabaseManager.DataSource, sessionFactory);
```

## Design Principles

- **Service tests**: Mock repositories, use `FakeDbSession` - no database needed
- **Repository tests**: Use `TestDatabaseManager` - real database with automatic rollback
- **Transactions for isolation**: Each test runs in its own transaction that never commits
- **Database shared across tests**: Isolation is maintained through transactions
- **Never commit in tests**: Transactions roll back automatically on disposal

## Test Naming Conventions

> The following test naming conventions aren't specific to this library and not something that is required by this library. It's just a general note on testing conventions.

All test methods should follow the three-part naming convention: `MethodBeingTested_Scenario_ExpectedBehavior`

- **MethodBeingTested**: The name of the method being tested
- **Scenario**: The scenario under which the method is being tested (typically starts with "When")
- **ExpectedBehavior**: The expected behavior when the scenario is invoked (typically starts with "Returns", "Throws", "Creates", etc.)

**Examples:**
```csharp
public async Task CreateAsync_WhenValidData_ReturnsNewId()
public async Task GetBasicAsync_WhenNotFound_ReturnsNull()
public async Task UpdateAsync_WhenInvalidId_ThrowsArgumentException()
```


## Per-Checkout Test Databases

`CheckoutDatabaseIdentity` derives the test database a checkout owns from its repository root path, so the suites of concurrent git worktrees never share (and never corrupt) one database:

```csharp
private static readonly CheckoutDatabaseIdentity Identity = new CheckoutDatabaseIdentity
{
    DatabasePrefix = "myproject_test_",
    PinEnvironmentVariableName = "MYPROJECT_TEST_DATABASE_NAME",
    CheckoutCommentPrefix = "myproject-test-checkout:",
    RepositoryRootFileName = "MyProject.sln",
};

string databaseName = Identity.ResolveDatabaseName(); // myproject_test_<8 hex chars>, or the pinned name
```

The derivation is a SHA-256 of the canonical checkout path truncated to 8 hex characters, simple enough to mirror in a non-.NET harness (a Playwright global setup, for example) so both suites in one checkout land on the same database. Setting the pin environment variable bypasses the derivation — for CI, or for pointing a local run at a specific database.

`RepositoryRoot` (walk up to the directory containing the solution file) supports the derivation and is usable on its own. The name validation the derivation relies on lives in `SqlIdentifier`, in the core [EasyReasy.Database](../EasyReasy.Database/README.md) package, because every identifier interpolated into SQL needs it — not only test database names.

For actually provisioning the database on PostgreSQL — creation, ownership stamping for safe pruning, and cluster-wide role bootstrap under an advisory lock — see [EasyReasy.Database.Testing.Npgsql](../EasyReasy.Database.Testing.Npgsql/README.md).
