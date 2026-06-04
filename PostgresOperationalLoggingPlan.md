# Plan: Extract Ordo's operational logging into the EasyReasy.Database family

> **Note on repo location.** This plan lives in the **`easy-reasy-database`** repo: the logging
> packages join the `EasyReasy.Database.*` family and reuse its agnostic data-access layer. The
> `EasyReasy.*` auth/resource packages are in the sibling `easy-reasy` repo.

## Goal

Lift Ordo's **operational** logging vertical (structured app/observability logs persisted to a
SQL database) into the EasyReasy.Database family as a **database-agnostic** package, with a thin
Postgres provider for the performance-sensitive bits. Then consume it from
[`bokur2`](../bokur2) and [`careless`](../careless).

**Scope is deliberately narrow: operational logs only.** Ordo's security-audit (ISO 27001) and
GMP / 21 CFR Part 11 record-audit paths are **out of scope** — they carry compliance semantics
(tamper-resistant role isolation, SECURITY DEFINER read/write functions, customer-frozen
retention) that don't belong in a general-purpose library and that neither target app needs.

## Architecture: agnostic core + Postgres provider

Mirror the existing `EasyReasy.Database` shape — an agnostic core (zero provider deps, works
against the ADO.NET / mapping abstraction) plus provider packages (`.Npgsql`, `.Sqlite`) that add
provider-specific behaviour.

Two things in Ordo's current design are genuinely Postgres-specific:

1. **The fast write path** — the operational sink uses Npgsql's binary `COPY FROM STDIN` for
   efficient batched bulk insert. That's the whole reason the sink is fast.
2. **Retention** — built on Postgres declarative partitioning (`PARTITION BY RANGE`, `pg_inherits`,
   monthly partition create/drop). SQLite has no equivalent.

Everything else is already provider-agnostic: the Serilog enrichers, the JSON write contract, the
options, the model types, the read-back repository (uses the agnostic mapping `QueryAsync`), the
in-memory broadcaster + SSE feed, the opt-in endpoint extensions, and the Serilog routing.

There's also a **web vs. non-web** axis: only two pieces touch ASP.NET — the `UserContextEnricher`
(reads `HttpContext` for user-id + IP) and the HTTP endpoints. The sink, trace-id enricher, host
wiring, read repo, and broadcaster are all web-free, and the maintainer runs headless worker
services that should be able to log to the DB without an ASP.NET dependency. So the web bits get
their own package.

## Package set

**Three shipping packages** for logging, plus a one-type addition to the existing database core:

| Package | Contents | Dependencies |
|---|---|---|
| **`EasyReasy.Database.Logging`** | Agnostic, **web-free** core: portable batched `INSERT` sink (via mapping `ExecuteAsync`), `TraceIdEnricher`, JSON contract, options, models, read repo, broadcaster, and the `AddOperationalLogging` / `UseOperationalLogging` wiring. | `EasyReasy.Database.Mapping` + **Serilog only** (no Npgsql, no ASP.NET) |
| **`EasyReasy.Database.Logging.Npgsql`** | Postgres fast path: binary `COPY FROM STDIN` sink + monthly-partition retention service + embedded DDL. | core + `EasyReasy.Database.Npgsql` + `Npgsql` |
| **`EasyReasy.Database.Logging.AspNetCore`** | Web bits: `UserContextEnricher` (HttpContext user/IP) + opt-in `MapOperationalLogEndpoints` (paginated read + SSE). | core + ASP.NET (`Microsoft.AspNetCore.App` framework ref) |

Plus: **`PagedResult<T>` added to the existing `EasyReasy.Database`** package (prerequisite PR).

**Deliberately not built:**

- **No `.Sqlite` package** — the agnostic core *is* the SQLite story (portable INSERT). The only
  Postgres-specific things are COPY perf + partition retention. (Optional nicety: the core could
  add an agnostic row-level DELETE retention — `DELETE WHERE created_at < cutoff` on a timer — that
  works on any provider, while `.Npgsql` keeps the more efficient partition-drop. Flag for later.)
- **No `.Testing` package** initially — consumers use the existing `EasyReasy.Database.Testing`.
- Test projects (`*.Logging.Tests`, `*.Logging.Npgsql.Tests`) are not shipped packages.

A Postgres web app references all three; a headless Postgres worker skips `.AspNetCore`; a SQLite
app references only the core.

## Key decisions (resolved)

1. **Naming: `EasyReasy.Database.Logging` (+ `.Npgsql`).** Joins the database family, reuses its
   mapping layer, and `PagedResult` is right there. (Minor: "logging" under the `Database.*`
   namespace reads slightly oddly since it's a logging concern that *uses* a database — acceptable
   for family grouping and the hard dependency direction. Alternative `EasyReasy.Logging` +
   `EasyReasy.Logging.Npgsql` in the same repo if we'd rather not nest under `Database`.
   **Low-stakes; confirm during impl.**)
2. **Database-agnostic core, Postgres provider package** — as above. Replaces the earlier
   "`EasyReasy.Serilog.Postgres`, Postgres-only" idea.
3. **Serilog is the one core third-party dependency.** Honest and unavoidable — the whole design is
   Serilog sinks/enrichers. Npgsql is *not* a core dep; it lives only in the `.Npgsql` package,
   exactly like the database family.
4. **Target framework: `net10.0`.** The maintainer runs .NET 10 everywhere; the
   `EasyReasy.Database.*` family is still on net8 only because it hasn't been bumped yet, not as a
   deliberate floor. A net10 package references the existing net8 database packages fine (net10
   consumes net8 libraries), so the new packages can be net10 now and slot in as the family moves
   that way. Cosmetic-only caveat: logging packages are net10 while their database deps stay net8
   until that sweep happens — a separate, non-blocking change.
5. **Web vs. non-web split, not a read/stream split.** The read repo + broadcaster are web-free
   and live in the core (agnostic via the mapping layer). Only the `HttpContext` enricher and the
   HTTP endpoints are web-coupled, so they go in `.AspNetCore` — keeping the core usable from
   headless worker services. (This replaces the earlier `.Admin` idea, which split along the wrong
   axis.) The React admin UI stays per-app regardless.
6. **Admin HTTP endpoints are optional + opt-in, mirroring `EasyReasy.Auth`.** Ship a
   `MapOperationalLogEndpoints(...)`-style extension (paginated read + SSE) the consumer opts into
   or replaces with their own, the same way `AddAuthEndpoints(...)` works in
   `EasyReasy.Auth/AuthApplicationBuilderExtensions.cs`.
7. **`PagedResult<T>` moves into `EasyReasy.Database`.** Clean generic type, pagination is a
   database-layer concern, and the read repo depends on the database core anyway. Small standalone
   prerequisite PR. (See "Cross-repo prerequisite".)

## Source inventory (Ordo → new packages)

All Ordo paths are under `Ordo.Server/Logging/`.

### Agnostic core — `EasyReasy.Database.Logging`

| Ordo source | Action | Notes |
|---|---|---|
| `Sinks/OperationalLogSink.cs` | **rewrite as portable INSERT sink** | Replace the binary-COPY body with a batched multi-row `INSERT` via mapping `ExecuteAsync`. Keep the `PeriodicBatchingSink` buffering + best-effort drop semantics. Broadcaster dependency optional. The COPY version moves to `.Npgsql`. |
| `Sinks/LogEventPropertyExtensions.cs` | move | Scalar extraction + JSONB serialization. Provider-agnostic. |
| `LoggingJsonOptions.cs` | move | camelCase JSON contract. |
| `LogFieldFormatter.cs` | move | Surrogate-safe truncation. |
| `Enrichers/TraceIdEnricher.cs` | move | Zero deps (`Activity.Current`, not ASP.NET). |
| `Constants/LoggingConstants.cs` (operational fields) | → options | Batch size / period / queue + table name + retention become `OperationalLoggingOptions`. |
| `HostBuilderExtensions.cs` | **rewrite** | Agnostic `UseOperationalLogging()` — Serilog provider wiring, routing by `SourceContext`, no audit sinks. |
| `ServiceCollectionExtensions.cs` | **rewrite** | Agnostic `AddOperationalLogging(options)` — registers the portable sink, enrichers, broadcaster, read repo. |
| `Admin/Repositories/AdminLogRepository.GetOperationalLogsAsync` | extract | Operational read only, via agnostic mapping `QueryAsync`. |
| `Admin/Broadcasting/IOperationalLogBroadcaster.cs` + `InMemoryOperationalLogBroadcaster.cs` | move | Bring `Shared/Broadcasting/InMemoryFanoutBroadcaster` along or generalize it. |
| `Admin/Models/OperationalLogEntry.cs`, `OperationalLogEvent.cs`, `OperationalLogFilters.cs` | move | |

### ASP.NET bits — `EasyReasy.Database.Logging.AspNetCore`

| Ordo source | Action | Notes |
|---|---|---|
| `Enrichers/UserContextEnricher.cs` | **refactor** | Reads `HttpContext` for user-id + IP. Decouple from `EasyReasy.Auth.GetUserId()` — make identity extraction a pluggable `Func<HttpContext,string?>` option (default may delegate to `GetUserId()`). Registered via an `AddOperationalLoggingHttpContext()` call this package adds. |
| SSE + paginated-read endpoints (operational portion of `Admin/Controllers/AdminLogController.cs`) | extract → opt-in `MapOperationalLogEndpoints(...)` | EasyReasy.Auth `AddAuthEndpoints` style; consumer opts in or wires their own against the broadcaster + read repo. |

### Postgres provider — `EasyReasy.Database.Logging.Npgsql`

| Ordo source | Action | Notes |
|---|---|---|
| `Sinks/OperationalLogSink.cs` (binary COPY body) | move | The fast `COPY FROM STDIN (FORMAT BINARY)` sink. Registered in place of the portable sink when the consumer opts into the Npgsql package. |
| `Partitions/PartitionMaintenanceService.cs` (operational arm only) | move + refactor | Monthly partition create + retention drop. Drop the two `maintain_*_audit_log_partitions()` audit calls. Table name + retention from options. |
| Migration `046` (`operational_log` table + 3 indexes + `PARTITION BY RANGE`) | extract to embedded SQL | Postgres-specific DDL. Consumer drops it into their own dbup sequence. Keep the `{parent}_{YYYY_MM}` partition-name convention co-located with the maintenance service so they can't drift. |

## Friction points

1. **Portable sink rewrite.** The agnostic sink can't use binary COPY — build a single multi-row
   `INSERT ... VALUES (…),(…),…` with bound parameters and run it through mapping `ExecuteAsync`.
   Mind parameter-count limits on large batches (chunk if needed). This is genuinely new code, not
   a move; the COPY version survives untouched in `.Npgsql`.
2. **`UserContextEnricher` decoupling** from `EasyReasy.Auth` — pluggable user-id resolver on the
   options. Default can still delegate to `GetUserId()` (both targets depend on `EasyReasy.Auth`).
3. **Constants → `OperationalLoggingOptions`** — table name, retention, batch size, period, queue.
4. **Migration ownership** — library ships the Postgres DDL as embedded SQL; each app owns its
   dbup numbering. Partition-name regex stays in the library next to the maintenance service.
5. **The agnostic sink uses the main app data source** (no restricted credential), so there's no
   role/env plumbing to port.

## Consumer wiring (target end-state, Postgres app)

```csharp
// references all three: EasyReasy.Database.Logging(.Npgsql)(.AspNetCore)
builder.Services.AddOperationalLogging(options =>
{
    options.TableName = "operational_log";
    options.Retention = TimeSpan.FromDays(90);
});
builder.Services.AddNpgsqlOperationalLogging();       // swaps in COPY sink + partition retention
builder.Services.AddOperationalLoggingHttpContext();  // HttpContext user/IP enricher (.AspNetCore)
builder.Host.UseOperationalLogging();                 // installs Serilog as the ILogger provider

app.MapOperationalLogEndpoints();                     // optional: paginated read + SSE feed
// + one dbup migration creating operational_log (from the library's embedded DDL)
```

A headless Postgres worker drops the `.AspNetCore` calls. Every existing
`_logger.LogInformation(...)` call site is unchanged.

## Testing

- **xUnit** (`[Fact]`/`Assert`), matching the `easy-reasy-database` test projects (note: the
  `easy-reasy` repo uses MSTest — this family does not).
- `EasyReasy.Database.Testing` gives a real Postgres harness — assert the COPY sink lands
  well-formed rows, the JSONB matches the camelCase contract, and partition create/drop behaves.
- Test the **portable INSERT sink** against SQLite (via `EasyReasy.Database.Sqlite`) to prove the
  agnostic path actually is provider-neutral.

## Cross-repo prerequisite

`PagedResult<T>` moves into **`EasyReasy.Database`** (no pagination type exists there yet) — a
small standalone PR landed *before* the read repo can consume it. Ordo's own
`Shared/Pagination/PagedResult` can later retire in favour of the library type as an independent
follow-up.

## Rough phasing & effort

Estimated **~2–3 focused days** (slightly up from before — the portable sink is new code, and
there are now two packages plus the SQLite test path).

1. **[DONE] Prereq:** `PagedResult<T>` added to `EasyReasy.Database` (`Pagination/`), with tests.
2. **[DONE] Core (`EasyReasy.Database.Logging`):** trace-id enricher, JSON contract, options,
   models, portable INSERT sink, read repo, broadcaster, the `AddOperationalLogging` /
   `UseOperationalLogging` entry points, package README. SQLite portability tests green (write +
   read + broadcast). net10, builds + packs.
3. **[DONE] Provider (`EasyReasy.Database.Logging.Npgsql`):** binary COPY sink (`jsonb`-native),
   partition-maintenance hosted service, `OperationalLogSchema.CreateTableSql` (partitioned DDL +
   `DEFAULT` partition), `AddNpgsqlOperationalLogging()`, README. Shared `LogEvent` materialization
   extracted to a public `OperationalLogEventFactory` in the core so both sinks reuse it. Postgres
   tests green (COPY round-trip + partition drop/keep). net10, builds + packs.
4. **[DONE] Web (`EasyReasy.Database.Logging.AspNetCore`):** `HttpContextEnricher` (user id via a
   pluggable resolver defaulting to the `NameIdentifier` claim — no `EasyReasy.Auth` dependency —
   plus client IP), `AddOperationalLoggingHttpContext()`, opt-in `MapOperationalLogEndpoints()`
   (paginated read + SSE, returns a `RouteGroupBuilder` so the consumer adds `.RequireAuthorization`),
   README. Uses a `FrameworkReference` to `Microsoft.AspNetCore.App`. 4 enricher tests green. net10.
   *Follow-up:* endpoint integration tests (WebApplicationFactory) — endpoints are thin wrappers
   over the already-tested read repo + broadcaster, so covered by inspection for now.
5. **Adopt:** wire into bokur2 and careless (separate PRs per repo), add their migrations, retire
   any ad-hoc logging.

### Decisions made during core implementation

- **Serilog 4 native batching, not the `Serilog.Sinks.PeriodicBatching` package.** Serilog 4 moved
  `IBatchedLogEventSink` into `Serilog.Core` (an `IReadOnlyCollection<LogEvent>` shape) and added
  built-in batching via `WriteTo.Sink(sink, BatchingOptions)`. The PeriodicBatching package is now
  the legacy path, so the sink targets `Serilog.Core.IBatchedLogEventSink` and the host wiring uses
  `BatchingOptions`. One fewer dependency.
- **Timestamps are `DateTime` (UTC), not `DateTimeOffset`.** SQLite stores a datetime as TEXT and
  the mapping deserializer can't cast string→`DateTimeOffset` (verified — the read threw). `DateTime`
  round-trips on both providers (`Convert.ChangeType` parses the SQLite string; Npgsql returns
  `DateTime` for `timestamptz`) and the operational log is uniformly UTC anyway. Applied to
  `OperationalLogEvent`, `OperationalLogEntry`, and `OperationalLogFilters.From/To`.
- **Portable sink chunks at 50 rows per `INSERT`** (50 × 9 cols = 450 params) to stay under SQLite's
  999-parameter ceiling; the Serilog batch size limit is separate (default 100).
- **`properties` is bound as JSON-in-text** by the portable sink (no `::jsonb` cast — that's
  Postgres-specific). The Npgsql COPY sink will bind `jsonb` natively.

## Remaining things to confirm during implementation

- Namespace nesting: `EasyReasy.Database.Logging` vs `EasyReasy.Logging` (low-stakes).
- Whether the embedded `operational_log` DDL is fully table-name-parameterized or documented fixed.
- Multi-row INSERT chunking threshold for the portable sink (parameter-count limits).
</content>
