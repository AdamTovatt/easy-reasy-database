← [Back to overview](../README.md)

# EasyReasy.Database.Logging.AspNetCore

[![NuGet](https://img.shields.io/nuget/v/EasyReasy.Database.Logging.AspNetCore.svg)](https://www.nuget.org/packages/EasyReasy.Database.Logging.AspNetCore/)

ASP.NET Core integration for [EasyReasy.Database.Logging](../EasyReasy.Database.Logging/README.md). Adds the two web-coupled pieces the core deliberately leaves out:

- an **`HttpContext` enricher** that attaches the acting user id (as the `UserId` column) and the client IP (into `properties`) to operational log events;
- **opt-in admin endpoints** — a paginated read and a Server-Sent Events live feed.

## Installation

```bash
dotnet add package EasyReasy.Database.Logging.AspNetCore
```

## Enrichment

```csharp
builder.Services.AddOperationalLogging();
builder.Services.AddOperationalLoggingHttpContext(options =>
{
    // Default reads the standard NameIdentifier claim; override for a different claim:
    // options.ResolveUserId = ctx => ctx.User?.FindFirst("sub")?.Value;
});
builder.Host.UseOperationalLogging();
```

The enricher registers itself as an `ILogEventEnricher`, so `UseOperationalLogging` picks it up automatically. For the real client IP behind a reverse proxy, configure `UseForwardedHeaders` in your app as usual.

## Admin endpoints (opt-in)

```csharp
app.MapOperationalLogEndpoints()           // default prefix: /api/admin/logs
   .RequireAuthorization("Admin");         // your policy — the library does not gate
```

This maps:

- `GET /api/admin/logs/operational` — paginated read. Query: `level`, `sourceContext`, `from`, `to`, `page`, `perPage`. Returns a `PagedResult<OperationalLogEntry>`. `page` defaults to 1; `perPage` defaults to 50 and is clamped to a maximum of 100 (a larger request is silently capped — `TotalCount` still reports the full match count).
- `GET /api/admin/logs/operational/stream` — Server-Sent Events; every persisted row is pushed as a `data:` frame. Idle connections receive a `: heartbeat` comment frame roughly every 30 seconds to keep the connection alive through proxies; spec-compliant clients ignore comment frames. Pass `MapOperationalLogEndpoints(heartbeatInterval: ...)` to change the cadence — lower it if a reverse proxy closes idle connections in under 30 seconds.

On both payloads the `properties` field is serialized as a nested JSON object (the structured Serilog properties), not as an escaped JSON string — parse it directly, no second `JSON.parse` needed.

Don't want the ready-made surface? Skip `MapOperationalLogEndpoints` and consume `IOperationalLogReadRepository` + `IOperationalLogBroadcaster` directly.
