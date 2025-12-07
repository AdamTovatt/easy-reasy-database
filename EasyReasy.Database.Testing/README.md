# EasyReasy.Database.Testing

Utilities for testing code that uses the EasyReasy.Database library.

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
- `FakeDbSessionFactory.CreateSessionCallCount` - Number of times `CreateSessionAsync()` was called
- `FakeDbSessionFactory.CreateSessionWithTransactionCallCount` - Number of times `CreateSessionWithTransactionAsync()` was called

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
        await using (IDbSession session = await TestDatabaseManager.CreateTransactionSessionAsync())
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

> This doesn't matter for this library, this is just a general note about testing conventions.

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

