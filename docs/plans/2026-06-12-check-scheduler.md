# Check Scheduler — Implementation Plan

**Date:** 2026-06-12
**Service:** control-plane/
**README checklist item:** `- [ ] Check scheduler BackgroundService: pick due monitors, run HTTP checks concurrently, store results`

---

## Goal

Implement a background HTTP check scheduler that:

- Periodically queries enabled HTTP monitors whose last check is overdue.
- Runs overdue checks concurrently (bounded by a configurable limit).
- Persists a `CheckResult` row per check to the existing `check_results` table.
- Remains fully disabled during existing test runs via a config flag.

---

## Non-goals

- TCP monitor checks (runner is HTTP-only in this iteration; TCP monitors are excluded from the due-query).
- Incident lifecycle (open/close on consecutive failures) — a separate checklist item.
- SignalR broadcast of check results — a separate checklist item.
- Any new EF Core migration — the `check_results` table and its composite index on `(monitor_id, checked_at)` already exist.
- Changes to the agent metric payload or any out-of-scope service.

---

## Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | TCP monitors excluded from due-query this iteration | CheckRunner is HTTP-only; Tcp row would match due-filter but have no runner implementation |
| 2 | No new migration | `check_results` table is already migrated; no schema change required |
| 3 | Latency measured with `TimeProvider.GetTimestamp()` / `GetElapsedTime()` | Monotonic clock; avoids DateTime subtraction drift; required for FakeTimeProvider testability |
| 4 | Per-check DbContext scope | `DbContext` is not thread-safe; `Parallel.ForEachAsync` runs checks concurrently |
| 5 | `HttpClient.Timeout = InfiniteTimeSpan` | A linked `CancellationTokenSource` set to `TimeoutSeconds` solely governs timeouts, enabling precise error classification |
| 6 | Scheduler disabled by default in test fixture | Prevents scheduler from polluting the 29 existing deterministic tests |
| 7 | Fake HTTP targets via in-process Kestrel host | No WireMock dependency needed; stays within the approved stack |
| 8 | `CheckRunner` registered as singleton | Stateless; `IHttpClientFactory` is singleton-safe |
| 9 | `PeriodicTimer` first tick fires after one full period | With `PeriodSeconds=1` in integration tests this is acceptable (~1s wait) |
| 10 | `PulseApiFactory` extended with optional `IDictionary<string,string>` extra settings | Lets scheduler tests spin their own factory over the shared container DB with `Scheduler:Enabled=true` without touching the default factory |

---

## Steps

Each step ends with a verification command run from `C:\Dev\Pulse\control-plane\`.

---

### Step 1 — Add `SchedulerOptions` and wire config

**Files to create/modify:**

- CREATE `C:\Dev\Pulse\control-plane\src\ControlPlane.Api\Scheduling\SchedulerOptions.cs`
- MODIFY `C:\Dev\Pulse\control-plane\src\ControlPlane.Api\appsettings.json`

**What to do:**

Create `SchedulerOptions` as a plain POCO with three properties:

```
public class SchedulerOptions
{
    public bool Enabled { get; set; } = true;
    public int PeriodSeconds { get; set; } = 5;
    public int MaxConcurrency { get; set; } = 10;
}
```

Add the `Scheduler` section to `appsettings.json` alongside the existing `ConnectionStrings` and `Logging` sections:

```json
"Scheduler": {
  "Enabled": true,
  "PeriodSeconds": 5,
  "MaxConcurrency": 10
}
```

In `Program.cs`, bind the section before building the app:

```csharp
builder.Services.Configure<SchedulerOptions>(
    builder.Configuration.GetSection("Scheduler"));
```

**Verification:** `dotnet build` — no errors.

---

### Step 2 — Implement `DueMonitorSelector` (no DB, no HTTP)

**Files to create:**

- CREATE `C:\Dev\Pulse\control-plane\src\ControlPlane.Api\Scheduling\DueMonitorSelector.cs`

**What to do:**

`DueMonitorSelector` is a singleton that depends only on `TimeProvider`. It exposes one public method:

```csharp
public bool IsDue(Monitor monitor, DateTimeOffset? lastCheckedAt)
```

Logic:
- Return `true` when `lastCheckedAt` is `null`.
- Return `true` when `timeProvider.GetUtcNow() - lastCheckedAt.Value >= TimeSpan.FromSeconds(monitor.IntervalSeconds)`.
- Otherwise return `false`.

No database dependency. No `async`.

**Verification:** `dotnet build` — no errors.

---

### Step 3 — Write unit tests for `DueMonitorSelector`

**Files to create:**

- CREATE `C:\Dev\Pulse\control-plane\tests\ControlPlane.Tests\Scheduling\DueMonitorSelectorTests.cs`

**New test dependency (test project only, requires human approval before adding):**

- `Microsoft.Extensions.TimeProvider.Testing` — pin to latest stable at implementation time (researcher observed 10.x versions on NuGet). Add to `ControlPlane.Tests.csproj` only.

**Test cases (no `[Collection]` attribute — no Postgres needed):**

| Test name | Scenario | Expected |
|-----------|----------|---------|
| `NeverChecked_IsDue` | `lastCheckedAt` is `null` | `true` |
| `CheckedJustNow_IsNotDue` | `lastCheckedAt` = now, `IntervalSeconds` = 60 | `false` |
| `CheckedExactlyIntervalAgo_IsDue` | `lastCheckedAt` = now - 60s, `IntervalSeconds` = 60 | `true` (boundary inclusive) |
| `CheckedLongerAgo_IsDue` | `lastCheckedAt` = now - 90s, `IntervalSeconds` = 60 | `true` |
| `AdvancingTime_MovesMonitorToDue` | not due at T=0; `FakeTimeProvider.Advance(IntervalSeconds)` → due at T=interval | `false` then `true` |

All tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.

**Verification:** `dotnet test --filter "FullyQualifiedName~DueMonitorSelectorTests"` — 5 tests pass.

---

### Step 4 — Implement `CheckRunner`

**Files to create:**

- CREATE `C:\Dev\Pulse\control-plane\src\ControlPlane.Api\Scheduling\CheckRunner.cs`

**What to do:**

`CheckRunner` is injected as a singleton and depends on `IHttpClientFactory` and `TimeProvider`. It exposes:

```csharp
public Task<CheckResult> ExecuteAsync(Monitor monitor, CancellationToken ct)
```

Implementation details:

1. Create a `CancellationTokenSource` linked to `ct` with a timeout of `TimeSpan.FromSeconds(monitor.TimeoutSeconds)`. Dispose it in a `finally` block.
2. Obtain an `HttpClient` from `_httpClientFactory.CreateClient()`.
3. Set `client.Timeout = Timeout.InfiniteTimeSpan` so the CTS solely governs timeout.
4. Record `long startTimestamp = _timeProvider.GetTimestamp()` before the request.
5. Send `GET monitor.Url` with `HttpCompletionOption.ResponseHeadersRead` using the linked CTS token.
6. Compute `int latencyMs = (int)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds`.
7. Compute `CheckedAt` as `_timeProvider.GetUtcNow()` truncated to microseconds using the same precedent as `MonitorEndpoints.cs` line 34:
   `now.AddTicks(-(now.Ticks % (TimeSpan.TicksPerMillisecond / 1000)))`.

Outcome rules (populate a new `CheckResult` with `MonitorId = monitor.Id`):

| Condition | Status | StatusCode | LatencyMs | Error |
|-----------|--------|-----------|-----------|-------|
| Response status code matches `monitor.ExpectedStatusCode` | `Up` | actual code | measured | `null` |
| Response status code does not match | `Down` | actual code | measured | `"Expected status {expected}, got {actual}."` |
| `TaskCanceledException` AND the linked CTS was cancelled AND the caller `ct` was NOT cancelled | `Down` | `null` | `null` | `"Timed out after {monitor.TimeoutSeconds}s."` |
| `HttpRequestException` | `Down` | `null` | `null` | `exception.Message` |

The timeout error string must contain the words "timed out" (case-insensitive acceptable, but lowercase is fine). The connection-failure error is the raw `HttpRequestException.Message`, which is structurally distinct from the timeout string.

**Verification:** `dotnet build` — no errors.

---

### Step 5 — Implement `CheckScheduler` (`BackgroundService`)

**Files to create:**

- CREATE `C:\Dev\Pulse\control-plane\src\ControlPlane.Api\Scheduling\CheckScheduler.cs`

**What to do:**

`CheckScheduler` extends `BackgroundService` and is injected with `IServiceScopeFactory`, `DueMonitorSelector`, `CheckRunner`, `IOptions<SchedulerOptions>`, `TimeProvider`, and `ILogger<CheckScheduler>`.

`ExecuteAsync(CancellationToken stoppingToken)` logic:

```
if (!options.Enabled)
{
    logger.LogInformation("Scheduler is disabled.");
    return;
}

using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.PeriodSeconds), timeProvider);

while (await timer.WaitForNextTickAsync(stoppingToken))
{
    try
    {
        await RunTickAsync(stoppingToken);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error in scheduler tick.");
        // continue — transient DB errors must not kill the service
    }
}
```

`RunTickAsync`:

1. Create a scope, resolve `PulseDbContext`.
2. Query enabled HTTP monitors with their latest `CheckedAt` in one EF query:
   ```csharp
   db.Monitors
     .AsNoTracking()
     .Where(m => m.Enabled && m.Type == MonitorType.Http)
     .Select(m => new
     {
         Monitor = m,
         LastCheckedAt = db.CheckResults
             .Where(r => r.MonitorId == m.Id)
             .OrderByDescending(r => r.CheckedAt)
             .Select(r => (DateTimeOffset?)r.CheckedAt)
             .FirstOrDefault()
     })
     .ToListAsync(stoppingToken)
   ```
3. Filter results with `DueMonitorSelector.IsDue(row.Monitor, row.LastCheckedAt)`.
4. Run due checks concurrently using `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = options.MaxConcurrency`:
   - Each iteration creates its own `IServiceScope` and resolves its own `PulseDbContext`.
   - Calls `await CheckRunner.ExecuteAsync(monitor, stoppingToken)`.
   - Saves the returned `CheckResult` via `db.CheckResults.Add(result); await db.SaveChangesAsync(stoppingToken)`.
   - Wraps the per-check body in a `try/catch` that logs and continues so one failing check does not abort the batch.
5. Dispose each per-check scope when the iteration's `ValueTask` completes.

**Verification:** `dotnet build` — no errors.

---

### Step 6 — Wire everything into `Program.cs`

**Files to modify:**

- MODIFY `C:\Dev\Pulse\control-plane\src\ControlPlane.Api\Program.cs`

**Registrations to add** (before `var app = builder.Build()`):

```csharp
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddHttpClient();
builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection("Scheduler"));
builder.Services.AddSingleton<DueMonitorSelector>();
builder.Services.AddSingleton<CheckRunner>();
builder.Services.AddHostedService<CheckScheduler>();
```

Add the necessary `using` for the `Scheduling` namespace.

**Verification:** `dotnet build` — no errors. `dotnet test` — existing 29 tests still pass (scheduler does not run because the test factory will disable it in Step 7).

---

### Step 7 — Extend `PulseApiFixture` / `PulseApiFactory` for scheduler tests

**Files to modify:**

- MODIFY `C:\Dev\Pulse\control-plane\tests\ControlPlane.Tests\Infrastructure\PulseApiFixture.cs`

**Changes:**

1. Add an optional `IDictionary<string, string>? extraSettings` parameter to `PulseApiFactory`'s constructor (default `null`).
2. In `ConfigureWebHost`, apply `UseSetting("Scheduler:Enabled", "false")` first, then iterate `extraSettings` (if any) and apply each via `UseSetting`, allowing callers to override the default.
3. Expose `_postgres.GetConnectionString()` from `PulseApiFixture` via a public property `ConnectionString` so scheduler integration test classes can construct their own `PulseApiFactory` pointing at the same container.
4. The fixture's own internal `_factory` continues to pass no extra settings — it therefore gets `Scheduler:Enabled=false` and all 29 existing tests remain unaffected.

**Verification:** `dotnet test` — 29 existing tests pass.

---

### Step 8 — Write integration tests for `CheckScheduler`

**Files to create:**

- CREATE `C:\Dev\Pulse\control-plane\tests\ControlPlane.Tests\Scheduling\CheckSchedulerTests.cs`

**No new test dependencies.** In-process Kestrel hosts use `WebApplication.CreateBuilder` + `UseUrls("http://127.0.0.1:0")`, which is available via `Microsoft.AspNetCore.Mvc.Testing` already referenced.

**Test class structure:**

- Decorated with `[Collection(nameof(PostgresCollection))]`.
- Implements `IAsyncLifetime`.
- Constructor takes `PulseApiFixture fixture`.
- `InitializeAsync`:
  1. Calls `fixture.ResetDatabaseAsync()`.
  2. Builds a local Kestrel host (`_fakeHttpTarget`) that maps:
     - `GET /ok` → `200 OK`
     - `GET /error` → `500 Internal Server Error`
     - `GET /slow` → waits 5 seconds (longer than the 1s monitor timeout) then returns `200 OK`
  3. Starts the host; discovers the actual bound port via `_fakeHttpTarget.Urls`.
  4. Constructs a `PulseApiFactory` over `fixture.ConnectionString` with extra settings:
     `{ "Scheduler:Enabled": "true", "Scheduler:PeriodSeconds": "1" }`
  5. Stores the factory as `_schedulerFactory`; accessing `_schedulerFactory.Services` starts the host and the scheduler `BackgroundService`.
- `DisposeAsync`:
  1. Disposes `_schedulerFactory` first (stops the BackgroundService and waits; tolerate `aspnetcore#50622` disposal quirk by wrapping in a short-timeout try/finally if needed).
  2. Stops and disposes `_fakeHttpTarget`.

**Helper method:** `PollForCheckResultAsync(Guid monitorId, TimeSpan overall, TimeSpan interval)` — opens a `DbContext` scope via `_schedulerFactory.Services`, polls `CheckResults` table for a row where `MonitorId == monitorId`, retries every `interval` until `overall` elapses, throws `TimeoutException` if no row appears.

**Test cases:**

| Test name | Monitor setup | Assertion |
|-----------|--------------|-----------|
| `HttpMonitor_OkEndpoint_StoresUpResult` | URL = `/ok`, ExpectedStatusCode = 200, IntervalSeconds = 5, TimeoutSeconds = 1 | Row: `Status = Up`, `StatusCode = 200`, `LatencyMs >= 0`, `CheckedAt != default` |
| `HttpMonitor_ErrorEndpoint_StoresDownResult_WithErrorMessage` | URL = `/error`, ExpectedStatusCode = 200, IntervalSeconds = 5, TimeoutSeconds = 1 | Row: `Status = Down`, `StatusCode = 500`, `Error` contains "200" and "500" |
| `HttpMonitor_SlowEndpoint_StoresDownResult_WithTimeoutMessage` | URL = `/slow`, ExpectedStatusCode = 200, IntervalSeconds = 5, TimeoutSeconds = 1 | Row: `Status = Down`, `StatusCode = null`, `Error` contains "timed out" (case-insensitive) |
| `DisabledMonitor_NoRowStored_WhileEnabledMonitorGetsRow` | Two monitors: one disabled pointing at `/ok`, one enabled pointing at `/ok` | Enabled monitor gets a row within timeout; disabled monitor has no row after the enabled one's row appears |

Monitor rows are inserted directly via a `DbContext` scope (not via the API) to avoid validation constraints (particularly `IntervalSeconds >= 5` and `TimeoutSeconds < IntervalSeconds` rules). Use IntervalSeconds = 5, TimeoutSeconds = 1 for all test monitors (satisfies validation, but inserted directly so validation is irrelevant).

Poll `overall` timeout = 30 seconds; poll `interval` = 500 milliseconds.

**Verification:** `dotnet test --filter "FullyQualifiedName~CheckSchedulerTests"` — 4 tests pass.

---

### Step 9 — Tick the README checklist item

**Files to modify:**

- MODIFY `C:\Dev\Pulse\control-plane\README.md`

Change:

```
- [ ] Check scheduler `BackgroundService`: pick due monitors, run HTTP checks concurrently, store results
```

to:

```
- [x] Check scheduler `BackgroundService`: pick due monitors, run HTTP checks concurrently, store results
```

This must be in the same commit as all other changes.

**Verification:** `dotnet test` — all tests (29 existing + 5 unit + 4 integration = 38) pass.

---

## Test plan

### Unit tests — `Scheduling/DueMonitorSelectorTests.cs` (5 tests, no Postgres)

1. `NeverChecked_IsDue` — null lastCheckedAt returns true.
2. `CheckedJustNow_IsNotDue` — lastCheckedAt = now, interval 60s, returns false.
3. `CheckedExactlyIntervalAgo_IsDue` — lastCheckedAt = now - 60s, interval 60s, returns true (boundary inclusive).
4. `CheckedLongerAgo_IsDue` — lastCheckedAt = now - 90s, interval 60s, returns true.
5. `AdvancingTime_MovesMonitorToDue` — FakeTimeProvider.Advance(IntervalSeconds) flips result from false to true.

### Integration tests — `Scheduling/CheckSchedulerTests.cs` (4 tests, Postgres)

1. `HttpMonitor_OkEndpoint_StoresUpResult` — proves the happy path: Up status, correct code, latency measured.
2. `HttpMonitor_ErrorEndpoint_StoresDownResult_WithErrorMessage` — proves non-matching status code is Down with descriptive error.
3. `HttpMonitor_SlowEndpoint_StoresDownResult_WithTimeoutMessage` — proves timeout is classified correctly and error message contains "timed out".
4. `DisabledMonitor_NoRowStored_WhileEnabledMonitorGetsRow` — proves disabled monitors are excluded and enabled ones are still checked.

### Regression

All 29 existing tests must continue to pass without change. The scheduler is silenced in the default fixture by `Scheduler:Enabled=false`.

---

## Risks

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| `WebApplicationFactory` + `BackgroundService` disposal deadlock (aspnetcore#50622) | Medium | Wrap `_schedulerFactory.DisposeAsync()` in a `Task.WhenAny` with a short timeout (e.g. 10s); log a warning and proceed if it exceeds the timeout |
| `PeriodicTimer` first tick fires after one full period, not immediately | Low | With `PeriodSeconds=1` in tests the 30s overall poll timeout absorbs this easily |
| `Parallel.ForEachAsync` exception aggregation swallowing inner exceptions | Low | Per-check try/catch logs individually; outer tick-level try/catch logs the aggregate; IsDue filter reduces batch size |
| In-test Kestrel host port collision | Very low | `UseUrls("http://127.0.0.1:0")` lets the OS assign a free port |
| EF correlated subquery (`OrderByDescending().Select().FirstOrDefault()`) translating poorly on Postgres | Low | Verify generated SQL in test run logs; if needed, rewrite as `Max()` on `CheckedAt` (nullable DateTimeOffset) which translates to a standard SQL aggregate |
| `FakeTimeProvider` package version incompatible with .NET 10 | Low | Pin to a 10.x version; verify `dotnet restore` succeeds before writing tests |
| Scheduler running checks against real URLs in integration tests | Not applicable | Fake Kestrel host is used; no real network egress |

---

## Rollback

If the feature needs to be reverted before the next commit:

1. Delete `C:\Dev\Pulse\control-plane\src\ControlPlane.Api\Scheduling\` (four files).
2. Revert `Program.cs` to remove the five new `builder.Services` registrations and the `Scheduling` using.
3. Revert `appsettings.json` to remove the `Scheduler` section.
4. Revert `PulseApiFixture.cs` to remove the `extraSettings` parameter and `ConnectionString` property.
5. Delete `C:\Dev\Pulse\control-plane\tests\ControlPlane.Tests\Scheduling\` (two test files).
6. Revert `ControlPlane.Tests.csproj` to remove the `Microsoft.Extensions.TimeProvider.Testing` reference.
7. Revert `control-plane/README.md` to uncheck the scheduler item.
8. `dotnet build` and `dotnet test` must show 29 tests passing.

No migration was added, so no database rollback is needed.

---

## Proposed commit message

```
feat(control-plane): check scheduler BackgroundService for HTTP monitors

- Add SchedulerOptions bound from config section "Scheduler"
  (Enabled, PeriodSeconds, MaxConcurrency)
- Add DueMonitorSelector: pure IsDue logic, FakeTimeProvider-testable
- Add CheckRunner: IHttpClientFactory + TimeProvider, classifies Up/Down/
  Timeout/ConnectionFailure, latency via monotonic timestamps, CheckedAt
  truncated to microseconds
- Add CheckScheduler BackgroundService: PeriodicTimer loop, one EF query
  projecting latest CheckedAt per monitor, Parallel.ForEachAsync with
  per-check DbContext scope
- Wire TimeProvider.System, AddHttpClient, AddHostedService in Program.cs
- Extend PulseApiFactory with optional extra settings; fixture exposes
  ConnectionString; scheduler disabled by default in tests
- Add 5 DueMonitorSelector unit tests (FakeTimeProvider)
- Add 4 CheckScheduler integration tests (in-process Kestrel fake targets)
- Tick README checklist item
```

---

## Human decisions required

1. **`Microsoft.Extensions.TimeProvider.Testing` version** — The plan calls for this package in `ControlPlane.Tests.csproj` only. It is not in the approved stack list in the service README. Human sign-off is required before it is added. At implementation time, confirm the latest stable version that targets `net10.0`.

2. **EF correlated subquery vs. `Max()`** — The plan uses `OrderByDescending(r => r.CheckedAt).Select(r => ...).FirstOrDefault()` to get the latest `CheckedAt`. If EF translates this to an inefficient query in practice, the alternative is `db.CheckResults.Where(r => r.MonitorId == m.Id).Max(r => (DateTimeOffset?)r.CheckedAt)`. Confirm which translation is acceptable or whether an explicit raw SQL hint is preferred.

3. **`BackgroundService` disposal tolerance in tests** — aspnetcore#50622 can cause `DisposeAsync` on `WebApplicationFactory` to hang when a `BackgroundService` is running. The plan proposes wrapping disposal in a timed `Task.WhenAny`. Confirm whether a hard timeout + warning is acceptable or whether a different pattern (e.g., `CancellationTokenSource` + explicit `StopAsync`) is preferred.

4. **Monitor insertion in integration tests** — The plan inserts test monitors directly via `DbContext` to bypass API validation constraints (specifically `IntervalSeconds >= 5` and `TimeoutSeconds < IntervalSeconds`). If inserting via the HTTP API is preferred for consistency, the test monitor values must satisfy those constraints; confirm which approach is preferred.

5. **`CheckRunner` singleton vs. scoped** — The plan registers `CheckRunner` as a singleton because it is stateless and `IHttpClientFactory` is singleton-safe. If the team prefers all scheduler types to be scoped (resolved inside each tick scope), this is a minor wiring change. Confirm the preferred lifetime.
