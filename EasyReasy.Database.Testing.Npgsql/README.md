← [Back to overview](../README.md)

# EasyReasy.Database.Testing.Npgsql

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Testing.Npgsql.svg)](https://www.nuget.org/packages/EasyReasy.Database.Testing.Npgsql/)

PostgreSQL-specific testing utilities for suites that run against a real PostgreSQL cluster. Together with `CheckoutDatabaseIdentity` from [EasyReasy.Database.Testing](../EasyReasy.Database.Testing/README.md), these let the test suites of concurrent git worktrees share one cluster without coordination: every checkout provisions and owns its own database, and the one thing databases cannot isolate — cluster-wide roles — is bootstrapped under a cluster-wide lock.

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
| `PostgresConnectionStrings` | Derives maintenance / per-database / unpooled variants of a configured connection string. |
| `TestDatabaseProvisioner` | Creates the run's database if missing (race-safe) and stamps the ownership marker on derived databases. |
| `PostgresClusterLock` | Cluster-wide advisory-lock mutex, held on the maintenance database over an unpooled connection. |
| `TestClusterRoles` | Idempotent, race-safe creation of shared cluster roles under the lock. |

### Pinning

Setting the pin environment variable (`MYPROJECT_TEST_DATABASE_NAME` above) bypasses the derivation: CI sets it because each runner has one checkout and provisions a database by name, and it is the escape hatch for pointing a local run at a specific database. A pinned database is never stamped with an ownership marker — the name belongs to whoever chose it, and stamping it would enrol it in a pruning sweep that later deletes it.

### Reclaiming abandoned databases

Deleting a worktree leaves its derived database behind. The ownership marker is what makes reclamation safe: a sweep drops only databases whose name has the derived shape (prefix + 8 lowercase hex characters) AND whose marker points at a checkout path that no longer exists. See [EasyReasy.Database.Testing.PruneTool](../EasyReasy.Database.Testing.PruneTool/README.md), which ships that sweep as the `prune-test-databases` dotnet tool.

### Advisory locks and pooling

`PostgresClusterLock` deliberately uses an unpooled connection, so a lock holder's session ends when the lock is released rather than going back to the pool still holding it. The reasoning is spelled out on `PostgresConnectionStrings.MaintenanceUnpooled`; the short version is that an advisory lock belongs to the *session*, and Npgsql 10 hands a pooled session back before resetting it. If you take advisory locks of your own on this cluster, do the same.

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
