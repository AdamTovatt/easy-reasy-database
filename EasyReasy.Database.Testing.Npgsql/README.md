← [Back to overview](../README.md)

# EasyReasy.Database.Testing.Npgsql

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Testing.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Testing.Npgsql/)

PostgreSQL-specific testing utilities for suites that run against a real PostgreSQL cluster, covering two things such a suite cannot do for itself.

**Sharing one cluster.** Together with `CheckoutDatabaseIdentity` from [EasyReasy.Database.Testing](../EasyReasy.Database.Testing/README.md), the test suites of concurrent git worktrees share one cluster without coordination: every checkout provisions and owns its own database, and the one thing databases cannot isolate — cluster-wide roles — is bootstrapped under a cluster-wide lock.

**Proving a race happened.** A concurrency test that cannot observe its own interleaving passes whether or not the interleaving occurred. `PostgresBackendObserver` reads the database's own signal — one backend parked on another backend's transaction — so the test can [prove it reached the race](#proving-a-concurrency-test-reached-its-interleaving) it asserts about.

## Installation

```bash
dotnet add package EasyReasy.Database.Testing.Npgsql
```

This package provisions a database it is *given* the name of; it does not derive one, and so does not depend on the package that does. The usage below pairs it with `CheckoutDatabaseIdentity`, which is the derivation — add that package too if you want the per-checkout naming:

```bash
dotnet add package EasyReasy.Database.Testing
```

## Why per-checkout databases

Two checkouts pointed at one database corrupt each other whenever either suite recreates its schema, and the symptoms (`cache lookup failed for type`, spurious cast errors, pass counts that differ between identical runs) read as flakiness rather than as collision — so the cost is usually paid in debugging time, not in an obvious failure. Deriving the database name from the checkout path removes the collision instead of guarding it. `CheckoutDatabaseIdentity` carries the full reasoning.

## Usage

The typical assembly-level fixture, run once per test run:

```csharp
private static readonly CheckoutDatabaseIdentity Identity = new CheckoutDatabaseIdentity
{
    DatabasePrefix = "myproject_test_",
    PinEnvironmentVariableName = "MYPROJECT_TEST_DATABASE_NAME",
    CheckoutCommentPrefix = "myproject-test-checkout:",
    RepositoryRootFileName = "MyProject.sln",
};

// A key all harnesses in this repository must share; any positive constant works.
private const long RoleBootstrapLockKey = 424242001;

// 1. Point the run at the database this checkout owns, keeping configured host/credentials.
string databaseName = Identity.ResolveDatabaseName();
string connectionString = PostgresConnectionStrings.WithDatabase(configuredConnectionString, databaseName);

// 2. Create it if missing. A DERIVED database is stamped with its owning checkout so a pruning
//    sweep can later reclaim it; a PINNED database belongs to whoever chose the name and is never stamped.
await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(
    connectionString,
    ownershipMarker: Identity.IsPinned() ? null : Identity.MarkerFor(Identity.CurrentCheckoutRoot()));

// 3. Bootstrap the cluster-wide roles (if the schema needs any) under the cluster-wide lock.
await TestClusterRoles.EnsureAsync(
    connectionString,
    migrationRole: "postgres",
    roles:
    [
        new TestClusterRole("myproject_audit_owner", "NOLOGIN", GrantToMigrationRole: true),
        new TestClusterRole("myproject_audit_writer", "LOGIN PASSWORD 'dev'"),
    ],
    lockKey: RoleBootstrapLockKey);
```

### What each type does

| Type | Purpose |
|------|---------|
| `PostgresConnectionStrings` | Derives maintenance / per-database / unpooled variants of a configured connection string, always keeping host and credentials. |
| `TestDatabaseProvisioner` | Creates the run's database if missing (race-safe) and stamps the ownership marker on derived databases. |
| `PostgresClusterLock` | Cluster-wide advisory-lock mutex, held on the maintenance database over an unpooled connection. |
| `TestClusterRoles` | Idempotent, race-safe creation of shared cluster roles under the lock. |
| `PostgresBackendObserver` | Watches one backend wait on another, so a concurrency test can prove it reached the interleaving it asserts about. |
| `BackendBlockObservation` | What the observer saw: the outcome, plus a sentence a caller can hand straight to an assertion message. |
| `BackendBlockOutcome` | Which of the three ways the wait ended — reached, finished without waiting, or timed out. |

### Pinning

Setting the pin environment variable (`MYPROJECT_TEST_DATABASE_NAME` above) bypasses the derivation: CI sets it because each runner has one checkout and provisions a database by name, and it is the escape hatch for pointing a local run at a specific database. A pinned database is never stamped with an ownership marker — the name belongs to whoever chose it, and stamping it would enrol it in a pruning sweep that later deletes it.

### Reclaiming abandoned databases

Deleting a worktree leaves its derived database behind. The ownership marker is what makes reclamation safe: a sweep drops only databases whose name has the derived shape (prefix + 8 lowercase hex characters) AND whose marker points at a checkout path that no longer exists. See [EasyReasy.Database.Testing.PruneTool](../EasyReasy.Database.Testing.PruneTool/README.md), which ships that sweep as the `prune-test-databases` dotnet tool.

### Advisory locks and pooling

`PostgresClusterLock` deliberately uses an unpooled connection, so a lock holder's session ends when the lock is released rather than going back to the pool still holding it. The reasoning is spelled out on `PostgresConnectionStrings.Unpooled`; the short version is that a pooled session outlives the `NpgsqlConnection` that borrowed it, because Npgsql 10 hands it back before resetting it — so anything whose *session* state matters, an advisory lock or a backend pid, needs the unpooled derivation. If you take advisory locks of your own on this cluster, do the same.

### Mirroring in a non-.NET harness

If the repository also has a non-.NET test harness (a Playwright suite, for example), mirror the derivation there. In full:

1. take the checkout's repository root as an **absolute** path, with any **trailing directory separator removed**;
2. hash its **UTF-8** bytes with **SHA-256**;
3. render the digest as **lowercase** hex and keep the **first 8** characters;
4. append that to the same database prefix.

```js
// Node equivalent of CheckoutDatabaseIdentity.ForCheckout
const hash = createHash("sha256").update(resolve(repositoryRoot), "utf8").digest("hex").slice(0, 8);
const databaseName = `${databasePrefix}${hash}`;
```

Step 3's case is the one that bites: `prune-test-databases` only recognises a name whose hash is lowercase hex, so a mirror emitting uppercase would create databases no sweep can ever reclaim. The rest is softer — agreement between the harnesses is a convenience, not a correctness requirement, and two harnesses that canonicalised the path differently would simply own two databases instead of one, still isolated from every other checkout.

The lock key and role definitions, however, MUST match exactly on both sides; pin them with a test in each harness.

## Proving a concurrency test reached its interleaving

A concurrency test that fires its tasks and waits for all of them passes whether or not the two statements ever overlapped — and the obvious shape for a read-then-act race passes against the *unfixed* code too. The reasoning is spelled out on `PostgresBackendObserver`; the short version is that the only reliable signal is the database's own, one backend parked on another backend's transaction.

The observer needs the backend pid of the connection the racing work runs on, so that work has to be handed a session the test owns rather than left to open its own. `RepositoryBase.UseSessionAsync` takes an optional `IDbSession` for exactly this, and `IDbSession.Connection` is the `DbConnection` underneath:

```csharp
await using NpgsqlConnection observer = new NpgsqlConnection(
    PostgresConnectionStrings.Unpooled(connectionString));
await observer.OpenAsync();

// The session the racing call will run on, so its backend pid is knowable up front.
await using IDbSession racingSession = await repository.CreateSessionWithTransactionAsync();
int racingPid = ((NpgsqlConnection)racingSession.Connection).ProcessID;

// The transaction whose lock the racing statement must park on, held open on purpose.
await using IDbSession blocking = await repository.CreateSessionWithTransactionAsync();
int blockingPid = ((NpgsqlConnection)blocking.Connection).ProcessID;
await repository.TakeTheContendedRowAsync(blocking);

Task racing = repository.DoTheRacingThingAsync(racingSession);
BackendBlockObservation observation;

try
{
    observation = await PostgresBackendObserver.WaitForBlockAsync(
        observer,
        waitingBackendPid: racingPid,
        blockingBackendPid: blockingPid,
        inFlightWork: racing,
        timeout: TimeSpan.FromSeconds(10));
}
finally
{
    // Release the contention and drain the parked work BEFORE failing, whatever happened.
    await blocking.RollbackAsync();
    await racing;
}

Assert.IsTrue(observation.BlockObserved, observation.Description);
```

Four details are load-bearing rather than incidental:

- **Both pids are pinned.** Asking only whether *something* is blocked by the blocking backend is a weaker question that an unrelated statement queueing behind the same lock also satisfies.
- **The in-flight work is watched alongside the poll.** Work that finishes without ever waiting has answered the question, so the wait ends there rather than burning the whole timeout.
- **It reports rather than throws.** The cleanup above has to run before the test fails; an observation delivered as an exception would have to be caught first, and the message it carries is the only thing that failure has to say.
- **Both pid-bearing connections stay open for the whole call.** A pid only means anything while its backend is alive, and a closed connection's pid reads exactly like a backend that never waited. Within a single call pooling makes no difference to that — but *across* tests it does, for the reason given under [advisory locks and pooling](#advisory-locks-and-pooling): a pooled session outlives the `NpgsqlConnection` that borrowed it, so a later test can be handed a backend whose pid an earlier one reasoned about. The package's own tests use `PostgresConnectionStrings.Unpooled` throughout for that reason.

`BackendBlockObservation.Outcome` distinguishes all three endings, which is what turns a failure into a diagnosis:

| Outcome | What it means |
|---|---|
| `Blocked` | The interleaving was reached. Whatever the test asserts next is about a race that actually happened. |
| `WorkFinishedWithoutBlocking` | The work ran to completion without ever waiting. Usually means the test sequenced itself around the race. |
| `TimedOut` | The work is still running and still not blocked. Usually means it is waiting on something other than the intended backend, or is slow for an unrelated reason. |

On the `TimedOut` path the description also reports whether each pid was still a live backend, since a departed pid produces exactly the same reading as a backend that simply never waited.
