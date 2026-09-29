# Phase 5: Operational backfill, compatibility and rollout verification - Research

**Researched:** 2026-09-29
**Domain:** Live PostgreSQL 17 integration verification (EF Core 10 + Npgsql 10, xUnit v3), operational backfill runbook
**Confidence:** HIGH for code paths, stack and test design; MEDIUM for runtime behaviors that only a live run can confirm (flagged per item)

<user_constraints>
## User Constraints

No `05-CONTEXT.md` exists (no discuss-phase was run). The constraints below come from the ROADMAP Phase 5 goal, the orchestrator brief and `CLAUDE.md`. They are **not** verbatim CONTEXT.md decisions. Treat "Locked" as binding until the user amends them.

### Locked Decisions (ROADMAP Phase 5 + orchestrator brief)
- Close the live PostgreSQL enablement gate for phases 1–4 with an **opt-in, repeatable** integration suite. It runs against a **user-provided NON-production PostgreSQL 17 server**.
- Docker is unavailable on this machine (verified: the `dockerDesktopLinuxEngine` pipe is missing). So there is **no Testcontainers dependency and no dependency on a local Docker daemon**.
- Throwaway databases are created and dropped for each run.
- Required coverage:
  - real migrations apply cleanly (Shops, the cpshopsdb equivalent; Account, the cpaccountdb equivalent);
  - unique and filtered indexes under concurrency;
  - RESTRICT FKs;
  - check constraints, including the widened `CK_PublicAddressAudit_Kind` (1..4) and the shared `PublicAddressChecks`;
  - advisory-lock serialization (Shops `7402100101`, Account `7402100401`);
  - savepoint rollback inside ambient (Wolverine/EF) transactions;
  - backfill dry-run, cursor, replay and collision on real SQL;
  - city delete with zones returns 409 and rolls back;
  - the ordering of the user-rename demotion against `IX_UserPublicAddresses_OwnerId`;
  - the Google retry after `ChangeTracker.Clear()`.
- Add a rollout runbook step for reviewing the dry-run backfill.
- **No production data is touched. No flag is enabled in any deployed environment without explicit authorization.**
- Keep it lean: the shortest working diff and no speculative abstraction. Add no new packages if that can be avoided. Do not break CI or `dotnet test CoffeePeek.slnx`.

### Claude's Discretion
- Where the suite lives, the skip mechanism, the env var name, the fixture design, test data and assertions.
- The format and location of the runbook step.

### Deferred Ideas (OUT OF SCOPE)
- External frontend, redirects and canonical links (Phase 6).
- Enabling `PublicAddresses__Enabled` in any deployed environment, and running production backfill.
- These AccountService enablement decisions: D-02 username-change cooldown, WR-02 post-commit refactor of the deletion handlers, WR-03 split of the rate-limit bucket. The runbook records them; the suite does not implement them.
- A CI job that runs the suite against a service container, and a rehearsal on a real production schema dump (listed as optional user decisions below).
</user_constraints>

## Project Constraints (from CLAUDE.md)

| Directive | Impact on this phase |
|---|---|
| .NET 10 / C# 14; do not change the stack | Tests use the existing xUnit v3 3.2.2, FluentAssertions 8.9.0, Moq 4.20.72 and Npgsql (transitive). No new packages. [VERIFIED: Directory.Packages.props] |
| Public `CoffeePeek.Contract` must not change without versioning | Test-only phase, no contract edits. If the Q5 Shops backfill 409 parity fix is chosen, it changes the error mapping only; the DTOs stay unchanged. |
| Do not rename `CoffePeek.*`, `Shops.Persistance`, `CoffeeShop.Moderation.*` | Use the existing names, e.g. `CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext`. |
| Schema changes only through EF migrations via the Makefile | None are expected. The suite applies the **existing** migrations programmatically to throwaway DBs only. |
| Test conventions: `{Class}Tests.cs`; namespace mirrors production; `TestContext.Current.CancellationToken`; `<Using Include="Xunit"/>` | Follow the style of the existing `CoffeePeek.*.Infrastructure.Tests/Persistence/*` tests. |
| Always use Context7 for library docs | Done for xUnit v3, EF Core, Npgsql and Wolverine (see Sources). |
| GSD workflow enforcement | Execution goes through `/gsd-execute-phase`. |
| Note: CLAUDE.md lists Wolverine 5.30, but `Directory.Packages.props` pins **WolverineFx 6.35.0** | CLAUDE.md is stale. This does not affect the suite, because the tests mimic Wolverine's transaction without hosting Wolverine. [VERIFIED: Directory.Packages.props] |

<phase_requirements>
## Phase Requirements (gate checklist; research-assigned IDs)

No requirement IDs were provided. These IDs map each pending item in `docs/api/public-addresses.md` "Enablement gate" step 2, plus the open items in the 01–04 VERIFICATION files, to a concrete test.

| ID | Gate item (source) | Research support |
|---|---|---|
| G-S0 | Shops migrations apply cleanly: fresh, and incrementally over legacy data (docs:7, 01-VERIFICATION:20) | `Database.MigrateAsync` and `IMigrator.MigrateAsync(target)`; pre-feature id `20260923120247_AddCoffeeZonePolygon` |
| G-S1 | `CK_{table}_Slug/Lifecycle/Revision/Timestamps`, unique `IX_{table}_Slug`, filtered `IX_{table}_OwnerId`, and `CK_PublicAddressAudit_Kind` accepting 3/4 (docs:20-21) | Raw Npgsql inserts, then assert `SqlState` and `ConstraintName` |
| G-S2 | RESTRICT FKs for all four registries; retire-before-delete ordering (docs:18) | `ExecuteDeleteAsync` bypasses the hook and must fail with 23503; EF `Remove` goes through the hook and must succeed |
| G-S3 | Concurrent "Минск" creations serialize on `7402100101` (docs:17) | Held ambient transaction plus a `pg_locks` waiter probe |
| G-S4 | Concurrent rename, and allocation uniqueness (docs:16, 02-VERIFICATION:20) | `PublicAddressService.ChangeAsync` in parallel: one success, one 409 |
| G-S5 | Rename A→B→A demotion flush on PG (spec "rename demotes before promoting") | Owned transaction, filtered index |
| G-S6 | Rename rolls back both steps on failure (spec) | An in-flight conflicting row forces 23505 after the demotion |
| G-S7 | Savepoint rollback inside an ambient transaction (docs:16) | A duplicate `IX_Shops_ModerationId` on shop creation leaves no address or audit row |
| G-S8 | City delete with zones returns 409 and the retirement rolls back (docs:19, 03-REVIEW WR-03) | Real `DeleteCityHandler` inside an ambient transaction |
| G-S9 | Zone publish, archive and republish with the real `Polygon` round-trip (InMemory could not do this) | `CoffeeZoneRepository.GetByIdAsync`, then `Publish()` |
| G-S10 | Backfill dry-run, cursor, replay and collision on real SQL (docs:4, 201-214) | Rehearsal fixture: legacy seed, then migrate, then page-by-page |
| G-S11 | Rollback rehearsal at data level (spec step 7; docs:234) | Disable the flag: 503, deletion still retires; re-enable: addresses resolve |
| G-A0 | `20260928134755_AddUserPublicAddresses` applies cleanly to a cpaccountdb equivalent (docs:33) | Fresh, and incremental from `20260921104154_AddAccountDeletionConfirmationSecurity` |
| G-A1 | `CK_UserPublicAddresses_*`, `IX_UserPublicAddresses_Slug`, `IX_UserPublicAddresses_OwnerId` (docs:24,27) | Raw inserts |
| G-A2 | `FK_UserPublicAddresses_Users_OwnerId` RESTRICT; hard-delete ordering (docs:26, 04-VERIFICATION:12) | `ExecuteDeleteAsync` must fail; EF `Remove` must succeed |
| G-A3 | Ivan/ivan/Иван registering concurrently get distinct slugs, serialized by `7402100401` (docs:24) | Held lock with 2 waiters |
| G-A4 | The rename demotion flush never violates `IX_UserPublicAddresses_OwnerId`; soft delete retires all rows on PG (docs:25) | `UpdateUsernameHandler` A→B→A inside an ambient transaction |
| G-A5 | A username unique violation leaves the address unchanged (docs:29); plus rollback after a successful demotion flush | `IX_Users_Username` conflict; in-flight conflict on the final insert |
| G-A6 | A duplicate-email registration leaves no address row (docs:30) | `IX_Users_Email` 23505 inside an ambient transaction, then commit, then 0 rows |
| G-A7 | The Google concurrent-creation retry succeeds after `ChangeTracker.Clear()` (docs:31) | Real `GoogleLoginHandler` in two sessions with a held lock |
| G-A8 | The user backfill cursor translates and pages on PG; a concurrent unique violation returns 409 (docs:32) | Rehearsal fixture plus an in-flight conflict |
| G-A9 | Rollback rehearsal for Account | Disable the flag: 503; deletion still retires |
| G-M1 | HTTP smoke through the gateway (docs:34) | **Manual** (runbook commands) |
| G-M2 | D-02, the lock-contention measurement and the rate-limit bucket (docs:37-38, 114) | **Manual user decisions** |
</phase_requirements>

## Summary

Every pending gate item can be tested at the **service and DbContext level** against a real PostgreSQL server, with **no new packages and no new projects**. The production write paths are all reachable from test code:
- the SaveChanges hooks (`ShopsDbContext.cs:32-49`, `AccountDbContext.cs:24-37`);
- the transaction helpers with the savepoint and advisory lock (`PublicAddressTransaction.cs:11-37`, `ProfileAddressTransaction.cs:11-37`);
- the services (`PublicAddressService`, `ProfileAddressService`);
- the real Wolverine handler bodies (`DeleteCityHandler`, `UpdateUsernameHandler`, `GoogleLoginHandler`, all `public static`).

The only piece of Wolverine that matters for database semantics is the ambient transaction. The generated handlers do `if (db.Database.CurrentTransaction == null) await db.Database.BeginTransactionAsync()`, run the handler, call `db.SaveChangesAsync()`, then `efCoreEnvelopeTransaction.CommitAsync()` (`GoogleLoginCommandHandler204426014.cs:48-73`). Tests reproduce this faithfully with `BeginTransactionAsync`, the handler call, `SaveChangesAsync` and `CommitAsync`. [VERIFIED: generated code; CITED: Wolverine EF Core INTERNALS.md]

Put the suite **inside the two existing test projects**: `CoffeePeek.Shops.Infrastructure.Tests` and `CoffeePeek.Account.Infrastructure.Tests`.
- Tag it `[Trait("Category","LivePostgres")]`.
- Gate it with `Assert.SkipUnless(LivePostgres.IsConfigured, …)` in each test-class constructor, which xUnit v3 supports for class-wide skips [CITED: xunit discussion #2580].
- The class fixture's `InitializeAsync` returns immediately when the env var is unset, so CI (`CoffeePeek.Backend.ci.slnf`) and `dotnet test CoffeePeek.slnx` compile the suite and report it as Skipped.
- Both assemblies keep hundreds of non-skipped tests, which also sidesteps the xUnit v3 MTP "all tests skipped → exit 8" behavior [CITED: xunit #3370]. These projects run in VSTest mode anyway.
- Npgsql 10.0.2 is already resolved transitively in both projects [VERIFIED: obj/project.assets.json].

The key technique that makes the concurrency tests **deterministic** is to observe the lock wait on the server instead of racing:
1. Session A saves inside an open ambient transaction, so it holds `pg_advisory_xact_lock`.
2. Session B is started.
3. The test polls `pg_locks` until B is waiting on that exact advisory key (bigint key = `(classid::bigint << 32) | objid::bigint`, `objsubid = 1`, `database` = current DB) [CITED: PG17 pg_locks docs].
4. A commits, then B's outcome is asserted.

The same probe for `wait_event = 'transactionid'` in `pg_stat_activity` [CITED: PG17 wait events] creates an "in-flight" unique violation without flakiness. That path is needed for the 409-on-concurrent-conflict items, because every supported writer takes the advisory lock, so only out-of-band writes can collide.

**Primary recommendation:**
- Add ~6 files: 1 shared helper linked into both projects, 2 fixtures and 3–4 test classes, all trait-gated and env-var gated.
- Read the admin connection string from `COFFEEPEEK_LIVE_PG`: a maintenance DB (`postgres`), a dedicated CREATEDB role, and **no password in the env var** (Npgsql reads `%APPDATA%\postgresql\pgpass.conf`).
- Create and drop `cp_live_<service>_<utc>_<8hex>` databases per fixture with `DROP DATABASE … WITH (FORCE)`.
- Extend `docs/api/public-addresses.md` with a page-by-page dry-run review step and a rollback rehearsal.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|---|---|---|---|
| Throwaway DB lifecycle (create, migrate, drop, sweep) | Test harness (Npgsql admin connection) | Database | Only the test process creates or drops, and only `cp_live_*` DBs it owns |
| Constraint, index and FK enforcement | Database / Storage | — | InMemory cannot prove these; the migrated schema must reject bad rows |
| Allocation, rename, retirement, lock and savepoint | API / Backend (EF SaveChanges hooks + transaction helpers) | Database (advisory lock, savepoints) | Exercised via `ShopsDbContext`/`AccountDbContext` plus the real handler bodies |
| Backfill dry-run, write and replay | API / Backend (`PublicAddressService`, `ProfileAddressService`) | Database (cursor SQL) | Service level; the controllers are thin pass-throughs (`AdminPublicAddressesController.cs:34-37`) |
| Gateway routing, query binding, ProblemDetails body | Frontend server / Gateway (YARP) + ASP.NET pipeline | — | Needs a running stack. **Manual** (G-M1) |
| Operator rollout (flags, backfill, review, rollback) | Ops / docs (`docs/api/public-addresses.md`) | Deploy env (`deploy/docker-compose.yml`) | Human-authorized; never automated here |

## Standard Stack

### Core (all already present, no installs)
| Library | Version | Purpose | Why Standard |
|---|---|---|---|
| xunit.v3 | 3.2.2 | Runner. `Assert.SkipUnless`, `IClassFixture`, `IAsyncLifetime` (ValueTask) | Project standard [VERIFIED: Directory.Packages.props]; dynamic skip and v3 lifetime [CITED: xunit.net v3 what's new/migration] |
| Npgsql | 10.0.2 (transitive via Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1) | Admin connection, raw inserts, `PostgresException.SqlState/ConstraintName`, `PostgresErrorCodes` | Already used by tests (`TransientDbConflictTests.cs:5`) [VERIFIED: project.assets.json] |
| Microsoft.EntityFrameworkCore(.Relational) | 10.0.5 | `MigrateAsync`, `IMigrator.MigrateAsync(target)`, `ExecuteDeleteAsync`, `BeginTransactionAsync` | Production ORM |
| FluentAssertions | 8.9.0 | Assertions | Project standard |
| Moq | 4.20.72 | `Mock.Of<ICacheService>()`, `IGoogleAuthService` stub | Already referenced by both test projects |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|---|---|---|
| Trait-gated tests in existing projects | New `CoffeePeek.PublicAddresses.LiveTests` project | Needs a csproj, slnx and ci.slnf edits and references to both bounded contexts. An all-skipped assembly exits 8 under MTP. Gains a single run command. **Not recommended** |
| Env-var + `Assert.SkipUnless` | `[Fact(Explicit = true)]` | Explicit tests need `-- xUnit.Explicit=on` in addition to credentials. More friction and no extra safety |
| Class-fixture DB per test class | Migrate once into a template DB, `CREATE DATABASE … TEMPLATE` per test | Full per-test isolation, but more code, and the template must have zero connections [CITED: PG17 CREATE DATABASE]. Use only if runtime becomes a problem |
| Service-level tests | In-process Wolverine host with Postgres persistence | Maximum fidelity for `EfCoreEnvelopeTransaction`, but needs host config, the `wolverine` schema and runtime codegen. The DB semantics are already reproduced by BeginTransaction/Commit |
| Testcontainers | — | Excluded: no Docker daemon (locked) |

**Installation:** none.

## Package Legitimacy Audit

No external packages are installed in this phase. Every dependency (xunit.v3, Npgsql, EF Core, FluentAssertions, Moq) is already referenced by the target test projects through `Directory.Packages.props` or transitively [VERIFIED: csproj + project.assets.json].

| Package | Registry | Age | Downloads | Source Repo | slopcheck | Disposition |
|---|---|---|---|---|---|---|
| (none new) | — | — | — | — | not run (nothing to install) | — |

**Packages removed due to slopcheck [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

## Architecture Patterns

### System Architecture Diagram

```
 developer / agent shell (Windows, .NET SDK 10.0.400)
   │  env COFFEEPEEK_LIVE_PG="Host=…;Database=postgres;Username=cp_live_tests"  (no password)
   │  %APPDATA%\postgresql\pgpass.conf  ──► password (Npgsql lookup)
   ▼
 dotnet test <Shops|Account>.Infrastructure.Tests --filter Category=LivePostgres
   │
   ├─ env var unset? ──► fixture InitializeAsync returns ─► class ctor Assert.SkipUnless ─► "Skipped" (CI path)
   │
   ▼ (set)
 LivePostgres helper ──admin conn (postgres DB)──► guards: not a prod DB name, PG≥17, CREATEDB, superuser policy
   │                                               sweep stale cp_live_* DBs owned by current_user
   │                                               CREATE DATABASE "cp_live_shops_<utc>_<8hex>"
   ▼
 LiveDatabase<TContext> fixture
   │  UseNpgsql(cs, EnableDynamicJson) + AuditInterceptor  (= production AddDatabase)
   │  [gate]      MigrateAsync()  ─────────────────────────────► fresh schema
   │  [rehearsal] Migrate(pre-feature) ► seed legacy rows (flag off) ► MigrateAsync() ► populated schema
   ▼
 tests (per gate item)
   ├─ raw Npgsql inserts ─────────────────► CHECK / UNIQUE / FK violations (SqlState + ConstraintName)
   ├─ Context(enabled) + BeginTransactionAsync (Wolverine-like ambient tx)
   │      └─ real handler / UoW.SaveChangesAsync ─► SaveChanges hook ─► RunAsync: SAVEPOINT ► pg_advisory_xact_lock ► writer ► base save
   ├─ second session Task ─► blocks on advisory lock ◄── probe pg_locks (deterministic barrier) ── commit first session
   ├─ in-flight blocker (raw tx inserts slug, uncommitted) ◄── probe pg_stat_activity wait_event='transactionid' ── commit blocker ─► 23505 path
   └─ PublicAddressService / ProfileAddressService.BackfillAsync (dry-run ► write ► replay, page by page)
   ▼
 fixture DisposeAsync (or InitializeAsync catch) ─► ClearAllPools ► DROP DATABASE … WITH (FORCE)  [only names matching cp_live_ regex]
```

### Recommended Project Structure
```
CoffeePeek.Shops.Infrastructure.Tests/
└── LivePostgres/
    ├── LivePostgres.cs                 # shared: env var, guards, create/drop/sweep, lock-wait probes, LiveDatabase<TContext> base
    ├── ShopsLiveDatabase.cs            # fixtures: gate (fresh) + rehearsal (legacy seed → latest)
    ├── ShopsLiveGateTests.cs           # G-S0..G-S9
    └── ShopsBackfillRehearsalTests.cs  # G-S10, G-S11 (one ordered scenario test)
CoffeePeek.Account.Infrastructure.Tests/
├── CoffeePeek.Account.Infrastructure.Tests.csproj   # + <Compile Include="..\CoffeePeek.Shops.Infrastructure.Tests\LivePostgres\LivePostgres.cs" Link="LivePostgres\LivePostgres.cs" />
└── LivePostgres/
    ├── AccountLiveDatabase.cs
    ├── AccountLiveGateTests.cs           # G-A0..G-A7
    └── AccountBackfillRehearsalTests.cs  # G-A8, G-A9
docs/api/public-addresses.md            # gate step 2 → "run the live suite"; backfill dry-run review; rollback rehearsal
```
Linking one source file keeps a single copy of the safety guards (which DBs may be dropped). Duplicating the ~120-line helper in both projects is acceptable if the planner prefers no cross-project link. Namespace: `CoffeePeek.Testing.LivePostgres`. Each assembly compiles its own copy, and there is no type conflict because the test assemblies don't reference each other.

### Pattern 1: Opt-in gate (fixture no-op plus constructor skip)
**What:** The fixture does nothing when unconfigured. The test class skips all its tests from its constructor.
**When:** Every live test class.
```csharp
// Source: xunit v3 dynamic skip (xunit.net/docs/getting-started/v3/whats-new; discussion #2580)
[Trait("Category", LivePostgres.Category)]
public sealed class ShopsLiveGateTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase _db;
    public ShopsLiveGateTests(ShopsLiveDatabase db)
    {
        Assert.SkipUnless(LivePostgres.IsConfigured, LivePostgres.SkipReason);
        _db = db;
    }
}
```

### Pattern 2: Throwaway database fixture that mirrors production options
```csharp
// Mirrors DatabaseModule.AddDatabase (UseNpgsql + EnableDynamicJson, DatabaseModule.cs:22-24) + AuditInterceptor (DependencyInjection.cs:48)
public abstract class LiveDatabase<TContext> : IAsyncLifetime where TContext : DbContext
{
    private DbContextOptions<TContext>? _options;
    public string ConnectionString { get; private set; } = "";
    protected abstract string Service { get; }                        // "shops" | "account"
    protected abstract TContext Create(DbContextOptions<TContext> options, bool enabled);
    protected virtual string? PreFeatureMigration => null;            // rehearsal fixtures only
    protected virtual Task SeedLegacyAsync(TContext db, CancellationToken ct) => Task.CompletedTask;

    public TContext Context(bool enabled = true) => Create(_options!, enabled);

    public async ValueTask InitializeAsync()
    {
        if (!LivePostgres.IsConfigured) return;                        // CI / unconfigured: no DB, no throw
        var ct = TestContext.Current.CancellationToken;
        ConnectionString = await LivePostgres.CreateDatabaseAsync(Service, ct);
        try
        {
            _options = new DbContextOptionsBuilder<TContext>()
                .UseNpgsql(ConnectionString, o => o.ConfigureDataSource(ds => ds.EnableDynamicJson()))
                .AddInterceptors(new AuditInterceptor())               // sets UTC CreatedAtUtc; Npgsql rejects Kind=Unspecified
                .Options;
            await using var db = Context(enabled: false);
            if (PreFeatureMigration is { } target)
            {
                await db.GetService<IMigrator>().MigrateAsync(target, ct);   // pre-feature schema (address tables absent)
                await SeedLegacyAsync(db, ct);                               // flag off: hooks never touch registries
            }
            await db.Database.MigrateAsync(ct);
        }
        catch
        {
            await LivePostgres.DropDatabaseAsync(ConnectionString);    // xUnit v3 skips DisposeAsync when InitializeAsync throws (#3124)
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (ConnectionString.Length > 0) await LivePostgres.DropDatabaseAsync(ConnectionString);
    }
}

public sealed class ShopsLiveDatabase : LiveDatabase<ShopsDbContext>
{
    protected override string Service => "shops";
    protected override ShopsDbContext Create(DbContextOptions<ShopsDbContext> o, bool enabled) =>
        new(o, Options.Create(new PublicAddressOptions { Enabled = enabled }));
}
```
Pre-feature targets:
- Shops: `20260923120247_AddCoffeeZonePolygon`.
- Account: `20260921104154_AddAccountDeletionConfirmationSecurity`.

The address migrations contain 0 `AddColumn/AlterColumn/DropColumn/RenameColumn/DropIndex/RenameIndex` [VERIFIED: grep]. So seeding legacy rows through the current model at the pre-feature schema is safe.

### Pattern 3: Deterministic serialization proof (held lock plus server-side probe)
```csharp
// Source: PG17 pg_locks ("Advisory locks are local to each database"; bigint key = (classid<<32)|objid, objsubid=1)
await using var a = _db.Context(enabled: true);
await using var txA = await a.Database.BeginTransactionAsync(ct);        // Wolverine-like ambient tx
var first = new City("Минск"); a.Cities.Add(first);
await a.SaveChangesAsync(ct);                                             // hook: SAVEPOINT, pg_advisory_xact_lock(7402100101), allocate "minsk"

var second = new City("Минск");
var b = Task.Run(async () =>
{
    await using var db = _db.Context(enabled: true);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    db.Cities.Add(second);
    await db.SaveChangesAsync(ct);                                        // blocks on the advisory lock
    await tx.CommitAsync(ct);
}, ct);

await LivePostgres.WaitForAdvisoryWaitersAsync(_db.ConnectionString, LivePostgres.ShopsLockKey, expected: 1, ct);
b.IsCompleted.Should().BeFalse("the second creation must wait on the advisory lock, not race");
await txA.CommitAsync(ct);
await b;
// assert: first → "minsk"; second → PublicSlug.Candidate("Минск", second.Id, 1)
```
```sql
-- WaitForAdvisoryWaitersAsync (poll every 50 ms, fail after 15 s)
SELECT count(*) FROM pg_locks
WHERE locktype = 'advisory' AND NOT granted AND objsubid = 1
  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
  AND ((classid::bigint << 32) | objid::bigint) = @key;
```

### Pattern 4: In-flight unique violation (the only way to hit the 409 mapping when every writer holds the lock)
```csharp
// Blocker: raw tx inserts the slug the service will pick, but does NOT commit yet
await using var x = new NpgsqlConnection(_db.ConnectionString); await x.OpenAsync(ct);
await using var xtx = await x.BeginTransactionAsync(ct);
await using (var ins = new NpgsqlCommand("""
    INSERT INTO "UserPublicAddresses" ("Id","OwnerId","Slug","State","Revision","CreatedAtUtc","UpdatedAtUtc")
    VALUES (@id, NULL, @slug, 3, 1, now(), now())
    """, x, xtx))
{ ins.Parameters.AddWithValue("id", Guid.NewGuid()); ins.Parameters.AddWithValue("slug", expectedSlug); await ins.ExecuteNonQueryAsync(ct); }

var run = Task.Run(() => new ProfileAddressService(ctx).BackfillAsync(null, 100, dryRun: false, ct), ct);
await LivePostgres.WaitForLockEventAsync(_db.ConnectionString, "transactionid", expected: 1, ct);  // service INSERT waits on x's tuple
await xtx.CommitAsync(ct);
(await run.Awaiting(t => t).Should().ThrowAsync<ConflictException>());   // ProfileAddressService.cs:97 → 409
// assert: registry contains only the blocker row (owned tx rolled back the whole page)
```
```sql
-- WaitForLockEventAsync
SELECT count(*) FROM pg_stat_activity
WHERE datname = current_database() AND wait_event_type = 'Lock' AND wait_event = @event;   -- 'advisory' | 'transactionid'
```
A non-superuser sees `wait_event` only for sessions of its own role, which covers all test sessions [CITED: PG17 monitoring-stats].

### Pattern 5: Savepoint proof (commit the ambient tx after the failure)
Production Wolverine rolls back the whole transaction after an unhandled exception. To isolate the **savepoint**, the test deliberately continues and **commits** the ambient transaction after catching the conflict:
1. It first proves the transaction is still usable: a query inside it succeeds rather than failing with `25P02` "current transaction is aborted".
2. After commit it proves nothing from the failed save persisted.

This is stricter than production, and it is exactly what the Google handler relies on: it catches `ConflictException` and saves again in the same transaction (`OAuthLoginHandler.cs:46-60`).

### Anti-Patterns to Avoid
- **Racing without a barrier:** `Task.WhenAll` of two saves and asserting "one waited" is flaky. Use the `pg_locks`/`pg_stat_activity` probes. Outcome-only parallel tests (the G-S4 rename race) are fine because the lock makes the outcome deterministic.
- **Sorting GUIDs client-side to predict cursor pages:** assert pages as sets (disjoint, union = all owners, ≤ limit, final `nextCursor` null), or get the expected order from `SELECT "Id" … ORDER BY "Id"` on the server.
- **One shared DB for backfill and other tests:** backfill pages cover *every* owner row in the table, so other tests' rows would leak in. Give the rehearsal its own fixture DB and one ordered scenario test (xUnit does not order methods by declaration).
- **Counting "Skipped" as passed:** a gate run is valid only with `Skipped: 0` for the LivePostgres filter.
- **Using `template1` as the maintenance DB:** `CREATE DATABASE` copies `template1` and fails while any session, including ours, is connected to it [CITED: PG17 CREATE DATABASE].

## Gate Item → Concrete Test Specification

Legend for the Deterministic column: **Y** = deterministic by construction, **Y/probe** = deterministic via a server-side wait probe.

### Shops (`ShopsLiveGateTests` on a fresh DB; `ShopsBackfillRehearsalTests` on a legacy-seeded DB)
| ID | Setup | Action | Assertion | Det. |
|---|---|---|---|---|
| G-S0 | Fixture migrated | `GetPendingMigrationsAsync()`, `GetAppliedMigrationsAsync()` | pending empty; applied == `Database.GetMigrations()`; contains `20260927112559_AddPublicAddressRegistries`, `20260928072406_AddPublicAddressAudit`, `20260928082712_AddCityAndZonePublicAddresses`. The rehearsal fixture also proves the pre-feature, then seed, then latest sequence | Y |
| G-S1 | Theory over the 4 tables; for the owner-index cases, owners created with a disabled context | Raw inserts: slug `Bad Slug`/`a--b`/101 chars; `State=1, OwnerId NULL`; `Revision=0`; `UpdatedAtUtc < CreatedAtUtc`; duplicate slug (tombstone vs tombstone); 2nd canonical for the same owner; canonical+alias for the same owner (OK); audit `Kind` 3 and 4 (OK), 0 and 5 (reject); duplicate audit `(Kind,OwnerId,Revision)` | `PostgresException.SqlState` 23514/23505 and `ConstraintName` = `CK_{t}_Slug`, `CK_{t}_Lifecycle`, `CK_{t}_Revision`, `CK_{t}_Timestamps`, `IX_{t}_Slug`, `IX_{t}_OwnerId`, `CK_PublicAddressAudit_Kind`, `IX_PublicAddressAudit_Kind_OwnerId_Revision` | Y |
| G-S2 | Owner of each kind created while enabled (it has a canonical row) | (a) `db.<Owners>.Where(id).ExecuteDeleteAsync()` (bypasses the hook); (b) EF `Remove` + `SaveChangesAsync` | (a) 23503 `FK_CoffeeShopPublicAddresses_Shops_OwnerId` / `FK_RoasterPublicAddresses_Roasters_OwnerId` / `FK_CityPublicAddresses_Cities_OwnerId` / `FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId`; (b) succeeds, all rows `Tombstone`, `OwnerId NULL`, a `retire` audit row exists (UPDATE ordered before DELETE). Also run (b) for a city **with the flag off**, because this path is live whatever the flag says | Y |
| G-S3 | Pattern 3 with two `City("Минск")` (optionally Theory: a Roaster pair too) | Held ambient tx A; B waits | Waiter observed on key `7402100101`; `minsk` and `Candidate("Минск", B.Id, 1)`; audit revision 1 for each | Y/probe |
| G-S4 | Shop S canonical rev 1; shops P, Q | Two `ChangeAsync(S, expectedRevision:1, slug x/y)` in parallel; separately two `ChangeAsync` of P and Q to the same slug | exactly one success and one `PublicAddressException` with `StatusCode == 409` in each pair; loser's rows unchanged | Y (lock serializes) |
| G-S5 | Shop canonical `a` | `ChangeAsync` a→b (rev 2), b→a (rev 3) | no 23505; the same row is restored; audit 1/2/3 | Y |
| G-S6 | Shop canonical `a` rev 1; blocker tx inserts uncommitted tombstone `b` | `ChangeAsync(a→b)`; probe `transactionid`; commit blocker | `PublicAddressException` 409 (`PublicAddressService.cs:80`); canonical `a` rev 1, no alias, audit only `initialize` (owned tx rolled back demotion **and** promotion) | Y/probe |
| G-S7 | Shop with `ModerationId` M committed | Ambient tx: add a 2nd shop with M (enabled) → `UnitOfWork.SaveChangesAsync` | `ConflictException` → inner 23505 `IX_Shops_ModerationId`; tx still usable; commit; 0 address/audit rows for the 2nd owner | Y |
| G-S8 | City C (address `…`, rev 1) + zone in C | Ambient tx: `DeleteCityHandler.Handle(new DeleteCityCommand(C), new CityRepository(db), new UnitOfWork<ShopsDbContext>(db), Mock.Of<ICacheService>(), ct)` | `ConflictException` (GlobalExceptionHandler → 409, `GlobalExceptionHandler.cs:64`), inner 23503 `FK_CoffeeZones_Cities_CityId`; within the tx and after commit: C exists, address Canonical rev 1, no `retire` audit. Characterization: another session's `pg_try_advisory_xact_lock(7402100101)` returns true before tx commit (lock released by savepoint rollback, see A3) | Y |
| G-S9 | Draft zone saved; new context | `CoffeeZoneRepository.GetByIdAsync` → `Publish()` → save; Archive; republish | allocated on first publish (Polygon round-trips on PG); archive hides (resolve null) without retiring; republish same row and revision | Y |
| G-S10 | Rehearsal fixture seeded at the pre-feature schema: 3 roasters `Same Name`; cities `Минск`, `Минск`, `Гродно`, `Гродна`; zones `Немига` Published in 2 cities + 1 Draft + 1 Archived; shops with name+address incl. one hidden | Per kind, page by page (`limit` small, e.g. 2): dry-run page → assert 0 registry/audit rows → write the same page → assert write slugs == dry-run slugs → continue with `nextCursor`; then replay page 1 | union of pages == all owners (zones: Published only; shops: hidden included); `HadCollision` exactly for the in-page duplicates; replay all `AlreadyInitialized`, no new rows or audit; last `nextCursor` null. **Pin the cross-page caveat**: a pure dry-run sweep does not flag duplicates that fall on different pages (the proposals set is per call, `PublicAddressService.cs:144`) | Y |
| G-S10b | Same fixture, a fresh kind page | Pattern 4 against Shops write-mode backfill | Today: raw `DbUpdateException` (23505) escapes → HTTP 500 (no mapping in `BackfillAsync`, `PublicAddressService.cs:126-139`; `GlobalExceptionHandler.cs:66`). Page rolled back. See **Q5** (recommended fix: 409 parity) | Y/probe |
| G-S11 | After G-S10 | Disabled context: resolve/batch/backfill → `PublicAddressException` 503; delete a city with the flag off → tombstones; add a city with the flag off → no address; re-enable → earlier slugs resolve | as stated | Y |

### Account (`AccountLiveGateTests` on a fresh DB with a seeded `Roles` row `User`; `AccountBackfillRehearsalTests` legacy-seeded)
| ID | Setup | Action | Assertion | Det. |
|---|---|---|---|---|
| G-A0 | Fixture migrated | as G-S0 | includes `20260928134755_AddUserPublicAddresses`; rehearsal proves incremental over populated `Users` | Y |
| G-A1 | Users created with the flag off (as owners) | Raw inserts as G-S1 on `UserPublicAddresses` | `CK_UserPublicAddresses_Slug/_Lifecycle/_Revision/_Timestamps`, `IX_UserPublicAddresses_Slug`, `IX_UserPublicAddresses_OwnerId` | Y |
| G-A2 | User registered while enabled | (a) `db.Users.Where(id).ExecuteDeleteAsync()`; (b) `db.Users.Remove(user)` + UoW save with the flag off | (a) 23503 `FK_UserPublicAddresses_Users_OwnerId`; (b) succeeds, the row is a tombstone (closes 04-REVIEW IN-05 "Before" ordering) | Y |
| G-A3 | — | Session A registers `Ivan` in an ambient tx (holds `7402100401`); B `ivan` and C `Иван` start; probe waits for **2** advisory waiters; commit A | A `ivan`; B `Candidate("ivan", B,1)`; C `Candidate("ivan", C,1)` (order-independent, because each suffix derives from the owner's own GUID) | Y/probe |
| G-A4 | `Ivan.Petrov` registered | Ambient tx: `UpdateUsernameHandler.Handle(new UpdateProfileUsernameCommand(id,"Petr"), new UserRepository(db), new UnitOfWork<AccountDbContext>(db), ct)` → Wolverine-style trailing save + commit; repeat back to `Ivan.Petrov`; then soft delete with the flag off | no `IX_UserPublicAddresses_OwnerId` violation; same row restored (rev 1→2→3); soft delete → both rows tombstone, shared revision | Y |
| G-A5 | A `Ivan.Petrov`, B `Petr` | (a) rename A→`Petr` in an ambient tx → `ConflictException` (23505 `IX_Users_Username`, raised in the demotion flush); (b) rename A→`Pavel` while a blocker holds an uncommitted `pavel` tombstone → probe `transactionid` → commit blocker | both: tx usable, commit; A's username unchanged, canonical `ivan-petrov` rev 1, no alias. (b) is the discriminating test: EF's own auto-savepoint only undoes the failing final batch, so only `ProfileAddressTransaction`'s savepoint undoes the already-flushed demotion (`ProfileAddressWriter.cs:111-114`) | Y / Y/probe |
| G-A6 | User with email E committed | Ambient tx: add a new user with E and username `Olga` (enabled) → UoW save | `ConflictException` (23505 `IX_Users_Email`); tx usable; commit; no `olga…` row | Y |
| G-A7 | Mocked `IGoogleAuthService` returns the same verified payload for both; real `ExternalAuthService`, `QueryUserRepository`, `RoleRepository`, `JWTTokenService` | Session A runs `GoogleLoginHandler.Handle` in an ambient tx (holds the lock, not committed); B starts; probe waits for B on `7402100401`; A: trailing save + commit; B finishes | B returns success via the `catch (ConflictException)` retry; one user with that email; one canonical row (A's); 2 refresh tokens; no row for B's discarded user | Y/probe |
| G-A8 | Rehearsal seeded at the pre-feature schema: `Ivan`, `ivan`, `Иван`, `admin`, a blocked user, a soft-deleted user | Page-by-page dry-run → write → replay; then Pattern 4 on a fresh page | deleted excluded, blocked included (and still hidden by resolve); collisions only for the suffixed; replay `AlreadyInitialized`; cursor pages as sets; in-flight conflict → `ConflictException` (`ProfileAddressService.cs:97`) and the page rolled back | Y, Y/probe |
| G-A9 | After G-A8 | Disabled: resolve/batch/backfill → `ServiceUnavailableException` (503); soft delete → still retires | as stated | Y |

### Manual (not automatable on this machine)
| ID | Why manual | How (runbook) |
|---|---|---|
| G-M1 HTTP/YARP smoke | Needs Gateway + AccountService + ShopsService + Redis + RabbitMQ; no Docker here. `Microsoft.AspNetCore.TestHost`/`Mvc.Testing` are not referenced | `curl -i $GW/api/Users/by-slug/reviews`: with the Account flag **off** a **503** ProblemDetails from AccountService proves routing (Shops would answer its `users/{userId}/reviews` route). With the flag on: 404 for unknown/hidden; `ids=` arrays bind (`?ids=a&ids=b`); hidden-profile 404 bodies identical **except `traceId`** (A5). Can run in the `dev/` Docker stack (AGENTS.md) or against staging with the flag off, subject to authorization |
| G-M2 decisions | Product/ops choices | D-02 cooldown; measure deletion lock-hold (WR-02); rate-limit bucket (WR-03) before enabling AccountService |

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---|---|---|---|
| Waiting for "the other session is blocked" | `Task.Delay` sleeps | `pg_locks` / `pg_stat_activity` probes | Sleeps are flaky on remote servers; the probe proves the *specific* lock |
| Applying the schema | Hand-written DDL / `EnsureCreated` | `Database.MigrateAsync`, `IMigrator.MigrateAsync(target)` | The gate is "real migrations apply cleanly"; `EnsureCreated` bypasses migrations [CITED: EF docs applying migrations] |
| Emulating Wolverine | A Wolverine test host | `BeginTransactionAsync` → handler → `SaveChangesAsync` → `CommitAsync` | Same DB semantics as the generated handlers (`…Handler204426014.cs:48-73`) |
| Killing leftover connections before DROP | Manual `pg_terminate_backend` loops | `DROP DATABASE … WITH (FORCE)` (PG13+) | Terminates our own sessions atomically; owner-only [CITED: PG17 DROP DATABASE] |
| Secret handling | Password in env var / appsettings / repo | pgpass (`%APPDATA%\postgresql\pgpass.conf`) or `PGPASSFILE` | Npgsql looks it up when `Password` is absent [CITED: npgsql security docs] |
| Constraint-name matching | Parsing error messages | `PostgresException.SqlState` + `ConstraintName`, `PostgresErrorCodes.*` | Stable. The production 409 mapping itself depends on `ConstraintName` (`ProfileAddressService.cs:117-120`) |

**Key insight:** the risky code (savepoint, advisory lock, rollback, `ChangeTracker.Clear`) is **skipped entirely under InMemory** (`if (!db.Database.IsRelational()) return await operation();`, `PublicAddressTransaction.cs:11`, `ProfileAddressTransaction.cs:11`). The first live run is the first time this code executes at all.

## Common Pitfalls

### Pitfall 1: The transaction helpers have never executed
**What goes wrong:** Savepoint creation and release, the advisory-lock SQL, rollback and `Clear()` have only ever been bypassed (InMemory).
**How to avoid:** Every G-S/G-A concurrency and rollback test must use a *relational* context and an ambient tx where production has one.
**Warning signs:** `25P02 current transaction is aborted` on the query after a caught conflict means the savepoint path failed.

### Pitfall 2: Npgsql rejects non-UTC `DateTime` for `timestamptz`
**What goes wrong:** `Entity<TId>.CreatedAtUtc` defaults to `DateTime.MinValue` (Kind Unspecified, `Entity.cs:10`), and inserts fail unless `AuditInterceptor` stamps UTC.
**How to avoid:** Always build test options with `.AddInterceptors(new AuditInterceptor())` (as production does), and use `now()` or `DateTime.UtcNow` in raw SQL. [ASSUMED Npgsql 6+ behavior; production parity avoids it either way]

### Pitfall 3: Cross-page duplicates are invisible in a dry-run-only sweep
**What goes wrong:** Both services keep `proposals` per call, and dry-run writes nothing. A duplicate stem on page 2 of an existing page-1 stem is reported as `hadCollision=false` in a dry-run sweep, but write mode would suffix it.
**How to avoid:** Runbook: **dry-run page N → review → write page N → dry-run page N+1**. Use `limit=100` to minimize page boundaries. The rehearsal test pins this behavior.

### Pitfall 4: Backfill pages see every owner in the table
**What goes wrong:** If backfill tests share a DB with other tests, unrelated rows appear in pages and get allocated.
**How to avoid:** A dedicated rehearsal fixture and one ordered scenario test per service.

### Pitfall 5: Connection pooling vs `DROP DATABASE`
**What goes wrong:** EF/Npgsql data-source pools keep idle connections to the throwaway DB, and a plain `DROP` fails with "being accessed by other users".
**How to avoid:** `NpgsqlConnection.ClearAllPools()` (best effort) plus `DROP DATABASE IF EXISTS "…" WITH (FORCE)` from the admin connection. It cannot run inside a transaction block, and not while connected to the target [CITED: PG17]. Build `DbContextOptions` once per fixture (one EF internal data source).

### Pitfall 6: Cleanup on failure
**What goes wrong:** xUnit v3 does not call `DisposeAsync` when `InitializeAsync` throws [CITED: xunit #3124], and Ctrl+C or a crashed test host leaves DBs behind.
**How to avoid:** Use a try/catch drop in `InitializeAsync`, and a start-of-run sweep limited to: `datname LIKE 'cp\_live\_%'`, owned by `current_user`, matching the exact name regex, and with the embedded UTC timestamp older than 6 h (so concurrent runs are safe). Document a manual cleanup SQL.

### Pitfall 7: Server prerequisites
- `pg_trgm` must be available (a contrib package). It is trusted, so the DB owner can `CREATE EXTENSION` [CITED: PG17 pg_trgm]. Required by migration `20260520052808_Update2025`.
- The DB encoding must be UTF8 (Cyrillic names). Assert `pg_encoding_to_char(encoding) = 'UTF8'` after `CREATE DATABASE`. Use a plain `CREATE DATABASE "x"` to match production's `init-databases.sql`.
- PG15+ `public` schema: the DB owner has CREATE. This holds because we create the DB ourselves.

### Pitfall 8: Parallel test classes
**What goes wrong:** xUnit runs classes in parallel.
**How to avoid:** Isolation comes from one DB per fixture, and advisory locks are per database, so there is no cross-talk [CITED: PG17 pg_locks]. Inside one class, tests must use disjoint stems: only G-S3 uses "Минск" and only G-A3 uses "Ivan". Set `Options=-c lock_timeout=30s` on the throwaway connection string so a bug fails fast instead of hanging.

### Pitfall 9: "Skipped" looks green
**What goes wrong:** If `COFFEEPEEK_LIVE_PG` is set with `setx` after the agent's shell started, the agent's process does not see it, so everything skips.
**How to avoid:** Gate evidence requires `Failed: 0, Skipped: 0` for `--filter Category=LivePostgres`.

### Pitfall 10: Flag-off Google in-handler retry (observation, not a gate item)
**What goes wrong:** With the flag **off**, the hook doesn't run, so `ChangeTracker.Clear()` doesn't happen. The failed `Added` user stays tracked and the in-handler retry re-inserts it. Recovery then relies on Wolverine's `TransientDbConflict` retry (`WolverineModule.cs:99-101`).
**How to avoid:** G-A7 runs with the flag **on**. Record this behavior as a note; do not change it in this phase. [ASSUMED from code reading]

### Pitfall 11: Maintenance DB choice
**What goes wrong:** Pointing `COFFEEPEEK_LIVE_PG` at `template1` breaks `CREATE DATABASE`. Pointing it at `cpshopsdb`/`cpaccountdb` risks confusion.
**How to avoid:** The helper refuses those names and requires a maintenance DB such as `postgres`.

## Code Examples

### Constraint rejection (raw insert helper)
```csharp
// Source: Npgsql PostgresException API; constraint names from the generated migrations
static async Task ExpectViolationAsync(NpgsqlConnection c, string table, Guid? owner, string slug, int state,
    long revision, DateTime created, DateTime updated, string sqlState, string constraint, CancellationToken ct)
{
    await using var cmd = new NpgsqlCommand($"""
        INSERT INTO "{table}" ("Id","OwnerId","Slug","State","Revision","CreatedAtUtc","UpdatedAtUtc")
        VALUES (@id, @owner, @slug, @state, @rev, @created, @updated)
        """, c);                                   // table comes only from [InlineData] constants
    cmd.Parameters.AddWithValue("id", Guid.NewGuid());
    cmd.Parameters.AddWithValue("owner", (object?)owner ?? DBNull.Value);
    cmd.Parameters.AddWithValue("slug", slug);
    cmd.Parameters.AddWithValue("state", state);
    cmd.Parameters.AddWithValue("rev", revision);
    cmd.Parameters.AddWithValue("created", created);   // DateTimeKind.Utc
    cmd.Parameters.AddWithValue("updated", updated);
    var ex = (await FluentActions.Awaiting(() => cmd.ExecuteNonQueryAsync(ct)).Should().ThrowAsync<PostgresException>()).Which;
    ex.SqlState.Should().Be(sqlState);          // PostgresErrorCodes.CheckViolation / UniqueViolation
    ex.ConstraintName.Should().Be(constraint);
}
```

### RESTRICT FK bypass vs supported delete
```csharp
var act = () => db.Cities.Where(c => c.Id == city.Id).ExecuteDeleteAsync(ct);    // bypasses the SaveChanges hook
(await act.Should().ThrowAsync<PostgresException>())
    .Which.Should().Match<PostgresException>(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation
                                               && e.ConstraintName == "FK_CityPublicAddresses_Cities_OwnerId");
```
(Depending on EF, `ExecuteDeleteAsync` may surface the `PostgresException` directly, not wrapped in `DbUpdateException`. Assert via `.Which` on whichever type is observed, and unwrap `InnerException` if wrapped.)

### Google retry (G-A7) handler call
```csharp
// GoogleLoginHandler is public static (OAuthLoginHandler.cs:11-72); construct deps like the generated handler (…Handler204426014.cs:36-42)
var google = Mock.Of<IGoogleAuthService>(g => g.ValidateIdTokenAsync("t", It.IsAny<CancellationToken>()) ==
    Task.FromResult<GoogleJsonWebSignature.Payload?>(new() { Email = email, EmailVerified = true, Subject = "sub-1" }));
Task<Response<GoogleLoginResponse>> Login(AccountDbContext db) => GoogleLoginHandler.Handle(new GoogleLoginCommand("t"),
    google, new ExternalAuthService(new QueryUserRepository(db), new RoleRepository(db)),
    new JWTTokenService(jwt), jwt, new UnitOfWork<AccountDbContext>(db), ct);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|---|---|---|---|
| Custom `SkippableFact` packages | `Assert.Skip/SkipUnless/SkipWhen`, `[Fact(SkipUnless=…)]` | xUnit v3 | No extra package [CITED: xunit v3 what's new] |
| `IAsyncLifetime` returning `Task` | `ValueTask` + inherits `IAsyncDisposable` | xUnit v3 | Fixture signatures [CITED: v3 migration] |
| EF 9: all pending migrations in one transaction | EF 10: one transaction per migration again | EF Core 10 | A failed historical migration leaves earlier ones applied; throwaway DB is dropped anyway [CITED: EF 9 breaking changes] |
| Terminate backends manually before DROP | `DROP DATABASE … WITH (FORCE)` | PG13 | One statement [CITED: PG17] |
| Superuser needed for extensions | Trusted extensions (pg_trgm) by DB owner | PG13 | A CREATEDB role suffices [CITED: PG17 pg_trgm] |

## Rollout Runbook Step (content outline for `docs/api/public-addresses.md`)

1. **Gate (non-production).** Run `dotnet test CoffeePeek.Shops.Infrastructure.Tests --filter Category=LivePostgres` and the Account equivalent, with `COFFEEPEEK_LIVE_PG` set. Record the date, `SHOW server_version`, the commit SHA and the counts (`Skipped: 0` is required). Replace each "pending" bullet in step 2 with its test name only after a green run.
2. **Pre-flight (target environment, authorized operator only).**
   - Take a `pg_dump` backup of `cpshopsdb` and `cpaccountdb`.
   - Confirm the prerequisite migrations with `SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 5;`, or run `deploy/scripts/apply-migrations.sh` (never `SKIP_MIGRATIONS=1`).
3. **Enable ShopsService only.** Set `PublicAddresses__Enabled=true` on `shopsservice` in the deploy env and restart it. Smoke test: `GET /api/Catalogs/cities/by-slug/<known>` returns 404 before backfill, not 503.
4. **Dry-run review, page by page, per entity** (`shops`, `roasters`, `cities`, `coffee-zones`). Use an admin JWT, and keep tokens out of chat and shell history:
   ```bash
   CURSOR=""; E=cities
   while :; do
     Q="limit=100${CURSOR:+&afterId=$CURSOR}"
     curl -sS -X POST "$GW/api/admin/$E/public-addresses/backfill?$Q&dryRun=true" -H "Authorization: Bearer $TOKEN" > "dry-$E-${CURSOR:-0}.json"
     jq '.items[] | select(.hadCollision or (.slug|test("^(place|user)(-|$)")))' "dry-$E-${CURSOR:-0}.json"   # review
     # unwanted slug? POST api/admin/$E/public-addresses/{id}/initialize {"slug":"…","expectedRevision":0,"reason":"…"} BEFORE writing
     curl -sS -X POST "$GW/api/admin/$E/public-addresses/backfill?$Q&dryRun=false" -H "Authorization: Bearer $TOKEN" > "write-$E-${CURSOR:-0}.json"
     diff <(jq -S '[.items[]|{entityId,slug}]' "dry-$E-${CURSOR:-0}.json") <(jq -S '[.items[]|{entityId,slug}]' "write-$E-${CURSOR:-0}.json")
     CURSOR=$(jq -r '.nextCursor // empty' "write-$E-${CURSOR:-0}.json"); [ -z "$CURSOR" ] && break
   done
   ```
   Review checklist:
   - duplicate city names;
   - RU/BY spelling variants (`grodno`/`grodna`, `mogilyov`/`magilyou`);
   - `place-…` fallbacks;
   - same-name zones in two cities.

   Why page by page: see Pitfall 3.
5. **Replay check.** Resubmit the first page with `dryRun=false`. Every item must report `alreadyInitialized`.
6. **AccountService.** Only after the D-02 decision, the lock-contention measurement and the rate-limit decision (G-M2):
   - enable it;
   - run the same loop against `/api/admin/users/public-addresses/backfill` (gateway catch-all `/api/admin/{**remainder}`); users have no initialize, so a slug changes only via a username change;
   - run the G-M1 HTTP smoke.
7. **Rollback rehearsal.**
   - Set `PublicAddresses__Enabled=false` and restart.
   - New endpoints must return 503, and GUID routes must be unchanged.
   - Deletions keep retiring.
   - **Never** run the migration `Down`: dropping populated registries loses reservations, and the Account `Down` succeeds destructively.

**What can be automated here:** only step 1 (the suite). Steps 2–7 touch deployed environments and need explicit authorization. The loop in step 4 is documentation, not a committed script (see Q7).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|---|---|---|
| A1 | `Assert.SkipUnless` thrown from a test-class constructor is reported as *Skipped* (not Failed) under the VSTest adapter used here | Pattern 1 | CI would go red. Fallback: move `Assert.SkipUnless` to the first line of each test. Check once with the env var unset |
| A2 | `dotnet test` (VSTest mode) exits 0 when all filtered tests are skipped | Validation | Only affects the local skip-check command; the full suites always have passing tests |
| A3 | A transaction-level advisory lock taken after a SAVEPOINT is released by `ROLLBACK TO SAVEPOINT` (PG docs state this for regular locks) | G-S8 characterization | If false, a failed save inside a long Wolverine tx keeps blocking writers until the outer tx ends. The characterization assertion reveals which is true; report it, don't fail the gate on it |
| A4 | EF Core orders the dependent UPDATE (retire, `OwnerId = NULL`) before the principal DELETE | G-S2/G-A2 | If wrong, a supported delete fails with 23503 in production. The live test is exactly the check |
| A5 | ASP.NET `NotFound()` ProblemDetails bodies differ only by `traceId` | G-M1 | Manual comparison procedure |
| A6 | Applying every historical Shops migration to an **empty** PG17 DB succeeds (production only ever applied them incrementally) | G-S0 | An old migration might fail on an empty DB for unrelated reasons. Record it as a separate pre-existing finding; the rehearsal fixture still proves the address migrations |
| A7 | The user's server ships `pg_trgm` (contrib) and uses UTF8 | Environment | Migration `Update2025` fails. The fixture should check `pg_available_extensions` first and give a clear message |
| A8 | Runtime per run is roughly 1–3 min (≈55 migrations × 4 fixtures over the network) | Validation | Latency only |
| A9 | Npgsql 6+ throws for `DateTime` Kind=Unspecified on `timestamptz` | Pitfall 2 | None if `AuditInterceptor` is used (production parity) |
| A10 | With the flag off, the Google in-handler retry fails and relies on Wolverine's retry | Pitfall 10 | Observation only; outside the gate |

## Open Questions (RESOLVED — Q1–Q10 adopted as recommended; see 05-CONTEXT.md D-01..D-10)

1. **Where does the suite live?**
   - Options: (a) trait-gated tests in the existing `Shops/Account.Infrastructure.Tests`; (b) a new `CoffeePeek.PublicAddresses.LiveTests` project.
   - **Recommended: (a).** No slnx/slnf changes, no new references, CI compiles it automatically, and the Shops `InternalsVisibleTo` stays available.
2. **Superuser policy for the test role.**
   - Options: (a) refuse superuser outright; (b) refuse unless `COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER=1` (for disposable containers such as the `dev/` stack); (c) allow any role and rely on the code-level guard (drop only own `cp_live_*` DBs).
   - **Recommended: (b).**
3. **Credential channel.**
   - **Recommended:** a user-level env var `COFFEEPEEK_LIVE_PG` **without** a password, e.g. `Host=<host>;Port=5432;Database=postgres;Username=cp_live_tests;SSL Mode=Prefer`, plus `%APPDATA%\postgresql\pgpass.conf` containing `<host>:5432:*:cp_live_tests:<password>`. Alternative: `PGPASSFILE` pointing at a file outside the repo.
   - Never commit it, and never paste it in chat.
   - Set it before starting the agent session: `setx` only affects new processes.
4. **Who runs the gate?**
   - **Recommended:** the executor runs it if the env var is visible in its shell (`[ -n "$COFFEEPEEK_LIVE_PG" ]`, without echoing the value).
   - Otherwise the user runs the two commands and pastes only the result summary lines, which contain no host or credentials.
5. **Shops write-mode backfill in-flight conflict returns 500 today** (G-S10b).
   - **Recommended:** add the Account-style `catch (DbUpdateException ex) when (IsAddressConflict(ex))` → `PublicAddressException(…, 409)` to `PublicAddressService.BackfillAsync` (a few lines, no contract change) and assert 409.
   - Alternative: keep 500 and document it.
6. **HTTP/YARP smoke (G-M1).**
   - **Recommended:** manual runbook commands in this phase.
   - Optional later: a `dev/public-addresses-smoke.sh` in the style of `dev/hello-world.sh`, runnable where Docker exists.
7. **Runbook tooling.**
   - **Recommended:** the curl/jq loop as a documentation snippet (no new script).
   - Alternative: `deploy/scripts/public-address-backfill.sh` that defaults to dry-run.
8. **Evidence after a green run.**
   - **Recommended:** update `docs/api/public-addresses.md` gate step 2 and STATE/ROADMAP with the date, PG version, commit and counts.
   - Flags stay unset everywhere.
9. **Permanent CI enforcement** via a GitHub Actions job with a `postgres:17` service container.
   - **Recommended: defer.** It is out of the locked scope, but a good follow-up for the "never comes back" core value. It needs Q2 (b).
10. **Rehearsal against a real production schema dump** (`pg_dump --schema-only`).
    - **Recommended: defer.** It needs production access (authorization), and PG17.6+ plain dumps contain psql meta-commands (`\restrict`) that Npgsql cannot execute [ASSUMED], while psql is not installed here.

### Exactly what the user must provide
1. **A NON-production PostgreSQL 17 server** reachable from this Windows machine: host, port, and SSL requirements (`SSL Mode=Require` if the server enforces TLS).
   - The `pg_trgm` extension must be available (standard contrib).
   - Server encoding must be UTF8.
2. **A dedicated login role**, created by the server admin. It must not be a member of any production role:
   ```sql
   CREATE ROLE cp_live_tests LOGIN CREATEDB NOSUPERUSER NOCREATEROLE NOREPLICATION PASSWORD '<secret>';
   ```
   - It can connect to `postgres` (the default PUBLIC CONNECT).
   - `pg_hba.conf` must allow `host all cp_live_tests <client-ip>/32 scram-sha-256`. It must be `all` databases, because each run creates new DB names.
   - No other grants are needed. The role owns the DBs it creates, so it can create `pg_trgm`, create schemas, and `DROP … WITH (FORCE)` only its own DBs.
3. **The connection string delivered out-of-band** (Q3): the env var without a password, plus `pgpass.conf`.
4. **Authorization boundaries confirmed:** the suite never connects to `cp*db` databases and never touches deployed configuration. Runbook steps 2–7 remain the user's explicit call.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|---|---|---|---|---|
| .NET SDK | build/test | ✓ | 10.0.400 (global.json 10.0.100, rollForward latestMajor) | — |
| Docker daemon | (not needed) | ✗ (`dockerDesktopLinuxEngine` pipe missing) | client 28.5.2 | Not required by design |
| Local PostgreSQL / `psql` | (not needed) | ✗ | — | Npgsql only; manual cleanup via the suite's sweep or pgAdmin |
| User-provided PG17 server | running the live gate | ✗ (not provided yet) | — | **None.** Tests can be written and CI-verified (skip path) without it; closing the gate is blocked until provided |
| `COFFEEPEEK_LIVE_PG` env var / pgpass | live gate | ✗ (not set) | — | Tests skip |
| `pg_trgm` on server | Shops migration `Update2025` | unknown | — | none (server admin installs contrib) |
| Running gateway/services stack | G-M1 | ✗ | — | Manual smoke elsewhere (`dev/` stack or staging with the flag off) |

**Missing dependencies with no fallback:** the user-provided PG17 server and role. They block closing the gate, not writing the suite.
**Missing with fallback:** Docker, psql (not needed); gateway stack (manual G-M1).

**External state created by the suite:**
- `cp_live_*` databases on the user's server, dropped per fixture and swept after 6 h.
- Nothing else: no roles, no files outside the user's pgpass/env.

## Validation Architecture

### Test Framework
| Property | Value |
|---|---|
| Framework | xUnit v3 3.2.2 + FluentAssertions 8.9.0 + Moq 4.20.72; VSTest adapter (xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.4.0) |
| Config file | none |
| Quick run (live) | `dotnet test CoffeePeek.Shops.Infrastructure.Tests --filter "Category=LivePostgres"` and `dotnet test CoffeePeek.Account.Infrastructure.Tests --filter "Category=LivePostgres"` |
| Skip-path check (no env var) | the same commands with `COFFEEPEEK_LIVE_PG` unset → `Failed: 0`, all Skipped |
| Full suite | `dotnet build CoffeePeek.Backend.ci.slnf -c Release --no-restore` then `dotnet test CoffeePeek.Backend.ci.slnf -c Release --no-build` (live tests skipped, everything else green) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|---|---|---|---|---|
| G-S0..G-S9 | Shops schema, concurrency, rollback | live integration | `dotnet test CoffeePeek.Shops.Infrastructure.Tests --filter "FullyQualifiedName~ShopsLiveGateTests"` | ❌ Wave 0 |
| G-S10, G-S11 | Shops backfill and rollback rehearsal | live integration (one scenario test) | `… --filter "FullyQualifiedName~ShopsBackfillRehearsalTests"` | ❌ Wave 0 |
| G-A0..G-A7 | Account schema, concurrency, rollback, Google retry | live integration | `dotnet test CoffeePeek.Account.Infrastructure.Tests --filter "FullyQualifiedName~AccountLiveGateTests"` | ❌ Wave 0 |
| G-A8, G-A9 | Account backfill and rollback rehearsal | live integration | `… --filter "FullyQualifiedName~AccountBackfillRehearsalTests"` | ❌ Wave 0 |
| CI safety | Suite skips cleanly and compiles in CI | build + skip run | skip-path check above + CI slnf Release build/test | ❌ Wave 0 |
| G-M1, G-M2 | HTTP smoke; enablement decisions | manual | runbook | n/a |

### Sampling Rate
- **Per task commit:** the touched project's full test run, including the InMemory suites. The live filter runs only if the env var is visible.
- **Per wave merge:** CI slnf Release build and test (live skipped).
- **Phase gate:** the CI slnf is green **and** one live run per project with `Failed: 0, Skipped: 0`, recorded with date, PG version and commit. Without the server, the phase can be verified only up to "suite implemented, skip path green, gate still open".

### Wave 0 Gaps
- [ ] `CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgres.cs`: env var, guards, create/drop/sweep, probes, `LiveDatabase<TContext>`.
- [ ] `ShopsLiveDatabase.cs` (gate and rehearsal fixtures); `AccountLiveDatabase.cs` (gate fixture seeds the `Roles` row `User`, as `dev/migrate-and-seed.sh` does).
- [ ] `<Compile Include=… Link=…>` in `CoffeePeek.Account.Infrastructure.Tests.csproj`.
- [ ] No framework install needed.

## Security Domain

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---|---|---|
| V2 Authentication | no (tests do not authenticate users; the Google payload is mocked) | — |
| V4 Access Control | yes (DB least privilege) | Dedicated CREATEDB role, NOSUPERUSER (Q2); ownership-scoped `DROP` |
| V5 Input Validation | yes (identifiers interpolated into DDL) | DB names generated internally, validated with `^cp_live_[a-z]+_[0-9]{8}t[0-9]{6}_[0-9a-f]{8}$` before quoting; table names only from constants; all values parameterized |
| V6 Cryptography | no | — |
| V8 Data Protection | yes | Synthetic data only. Never connects to `cp*db`. Tombstone/correlation caveats (docs:162) don't apply to throwaway data |
| V14 Configuration | yes (secrets) | No secret in the repo, env var or logs; pgpass outside the repo; never print the connection string (log the DB name only) |

### Known Threat Patterns
| Pattern | STRIDE | Standard Mitigation |
|---|---|---|
| Suite pointed at a production server drops or modifies data | Tampering | Refuse `cp*db`/template maintenance DBs. Drop only regex-matched, self-created, `current_user`-owned DBs. A non-superuser role cannot drop others' DBs (`DROP DATABASE` is owner-only [CITED]). Throwaway tables only |
| Credential leakage via chat, logs or git | Information disclosure | pgpass / `PGPASSFILE`; the agent checks presence only (`-n`), never echoes; Npgsql omits the password from exceptions by default |
| Leftover DBs accumulate | DoS (resource) | try/catch drop, `DisposeAsync` drop, and an age-limited sweep |
| Hung test holding locks on a shared server | DoS | `lock_timeout=30s` via `Options`, 15 s probe deadlines, cancellation tokens |

## Sources

### Primary (HIGH confidence)
- Codebase (file:line cited inline):
  - `ShopsDbContext.cs`, `AccountDbContext.cs`, `PublicAddressTransaction.cs`, `ProfileAddressTransaction.cs`, `PublicAddressService.cs`, `ProfileAddressService.cs`, `ProfileAddressWriter.cs`, `PublicAddressWriter.cs`;
  - `DatabaseModule.cs`, both `DependencyInjection.cs`, `UnitOfWork.cs`, `WolverineModule.cs`, `TransientDbConflict.cs`, `GlobalExceptionHandler.cs`;
  - the generated `GoogleLoginCommandHandler204426014.cs` and `RegisterUserCommandHandler748332044.cs`;
  - the migrations (names and constraint ids), `Directory.Packages.props`, `obj/project.assets.json`, `.github/workflows/ci-cd.yml`, `CoffeePeek.Backend.ci.slnf`, `deploy/*`, `dev/*`, `AGENTS.md`.
- Npgsql SQL translation of the cursor queries, run locally in a throwaway console outside the repo (`ToQueryString()` with the Npgsql provider). Output: `WHERE NOT (u."IsSoftDelete") AND u."Id" > @afterId_Value ORDER BY u."Id" LIMIT @p`, and the equivalents for zones (`c."Status" = 1 AND c."Id" > …`) and shops. [VERIFIED]
- Context7 `/websites/xunit_net`: v3 dynamic skip, `SkipUnless`/`SkipType`, `IAsyncLifetime` ValueTask/`IAsyncDisposable`, class/collection fixtures, Explicit.
- Context7 `/dotnet/entityframework.docs`: automatic savepoints in SaveChanges; migration locking; EF 9 single-transaction change reverted in EF 10; `MigrationsUserTransactionWarning`.
- Context7 `/websites/npgsql_doc`: pgpass lookup order (`Passfile` → `PGPASSFILE` → `%APPDATA%\postgresql\pgpass.conf`), env vars, `Options`, `ApplicationName`, `ClearAllPools`.
- Context7 `/jasperfx/wolverine`: EF Core INTERNALS (begin tx → handler → CommitAsync → SaveChanges → commit → flush).
- postgresql.org/docs/17: `sql-createdatabase` (CREATEDB, template restrictions, not in a tx block), `sql-dropdatabase` (FORCE, owner-only), `view-pg-locks` (advisory locks per database, bigint key layout), `monitoring-stats` (wait events `advisory`/`transactionid`, same-role visibility), `pgtrgm` (trusted), `explicit-locking` (savepoint rollback releases locks, stated for regular locks).
- learn.microsoft.com `dotnet/core/testing/selective-unit-tests`: xUnit trait filter `Category=…`.

### Secondary (MEDIUM confidence)
- github.com/xunit/xunit/discussions/2580: skip from the test-class constructor or fixture (maintainer).
- github.com/xunit/xunit/issues/3124: `DisposeAsync` not called when `InitializeAsync` throws.
- github.com/xunit/xunit/issues/3370: MTP exit code when all tests are skipped (by design).
- github.com/xunit/xunit/issues/3371: fixtures created for statically skipped tests.

### Tertiary (LOW confidence)
- PG17.6+ `pg_dump` `\restrict` meta-commands (Q10), from memory; not verified this session.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH. No new packages; versions read from the repo.
- Architecture and test design: HIGH. Every production path and signature was read, and the cursor SQL translation was verified.
- Runtime behaviors (EF statement ordering, lock release on savepoint rollback, fresh-from-zero migrations): MEDIUM. These are exactly what the live run determines, and they are flagged in the Assumptions Log.
- Pitfalls: HIGH for code-derived items; MEDIUM for environment-derived ones.

**Research date:** 2026-09-29
**Valid until:** 2026-10-29 (stable stack). Re-check if the Wolverine, Npgsql or EF versions in `Directory.Packages.props` change.
