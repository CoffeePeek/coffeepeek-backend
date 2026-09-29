---
phase: 05-operational-backfill-compatibility-and-rollout-verification
plan: 01
subsystem: testing
tags: [postgres, npgsql, xunit-v3, ef-core, live-tests, advisory-locks, test-harness]

# Dependency graph
requires:
  - phase: 01-04 (public slugs)
    provides: registry tables, lock keys 7402100101 / 7402100401, AuditInterceptor wiring
provides:
  - Shared opt-in live PostgreSQL harness CoffeePeek.Testing.LivePostgresServer (env gate, guards, create/drop/sweep, probes, raw registry helpers)
  - LiveDatabase<TContext> class-fixture base (production options, optional pre-feature migrate+seed, drop on success and failure)
  - Server-free guard tests (128 rows, run in CI) and 4 live harness self-tests (Skipped when unconfigured)
  - Account.Infrastructure.Tests links the single helper file
affects: [05-03, 05-04, 05-05, 05-06, 05-07, 05-08]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Live test class: [Trait(\"Category\", LivePostgresServer.Category)] + ctor Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason)"
    - "Blocked-session barrier: WaitForAdvisoryWaitersAsync / WaitForLockWaitsAsync, never Task.Delay"
    - "Sanitized connect: LivePostgresServer.OpenAsync rethrows connect failures with type/SqlState/socket codes only"

key-files:
  created:
    - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresServer.cs
    - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresGuardTests.cs
    - CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresHarnessTests.cs
  modified:
    - CoffeePeek.Account.Infrastructure.Tests/CoffeePeek.Account.Infrastructure.Tests.csproj

key-decisions:
  - "A1 verified: a constructor-level Assert.SkipUnless is reported as Skipped, both by the in-process xUnit v3 runner and by dotnet test (VSTest). Later plans keep the constructor skip; the per-method fallback is not needed"
  - "A2 verified: dotnet test exits 0 when every filtered test is skipped (Shops project; dotnet test is not blocked for that project on this machine)"
  - "OpenAsync is public (additive to the contract): later plans open connections through it so a connect error never prints the host"
  - "IsOwnedDatabaseName also enforces the 63-byte identifier limit; NewDatabaseName rejects services that would exceed it (PostgreSQL truncates longer names silently)"
  - "Probes, InsertRegistryRowAsync and BeginUncommittedTombstoneAsync refuse non-cp_live_* databases before connecting, like DropDatabaseAsync"
  - "The start-of-run sweep is best effort per database (a PostgresException on one stale DB is left for the next run); preflight failures always throw"

patterns-established:
  - "Fixture failure path: InitializeAsync catch -> DropAfterFailureAsync (AggregateException if the drop also fails) -> rethrow"
  - "Two-session tests: holder in await using blocks so it is disposed before the waiter is awaited; SettleAsync awaits the waiter in finally so no task outlives the test"

requirements-completed: [D-01, D-02, D-03, D-04]

# Metrics
duration: 16min
completed: 2026-09-29
---

# Phase 5 Plan 01: Live PostgreSQL harness Summary

**An opt-in, guarded harness (one shared `LivePostgresServer.cs`, linked into Account) that creates, migrates and drops only `cp_live_*` throwaway databases, fails when misconfigured, and ships deterministic `pg_locks`/`pg_stat_activity` blocked-session probes. 128 server-free guard rows run in CI; the 4 live self-tests report Skipped while `COFFEEPEEK_LIVE_PG` is unset.**

## Performance

- **Duration:** 16 min
- **Started:** 2026-09-29T13:07:41Z
- **Completed:** 2026-09-29T13:23:15Z
- **Tasks:** 2
- **Files modified:** 4 (3 created, 1 modified)

## Accomplishments

- **Pure guards (D-02, D-03), all tested without a server:**
  - owned-name regex `\A...\z` plus the 63-byte identifier limit;
  - admin parser: only exactly `Database=postgres`, no Password key, fixed messages with no inner exception, and the pgpass/PGPASSFILE fix named in the message;
  - superuser allowed only for exactly `"1"`;
  - 6-hour sweep age, rejecting impossible stamps;
  - registry-table allowlist;
  - `FindPostgres`.
- **Throwaway lifecycle (D-03, D-04):**
  - preflight: PG >= 17, superuser policy, CREATEDB, pg_trgm;
  - sweep of this role's own stale `cp_live_*` databases;
  - plain `CREATE DATABASE`, then a UTF8 check that drops the database on failure;
  - `DROP DATABASE IF EXISTS ... WITH (FORCE)` from an unpooled `postgres` connection, after a name guard that runs before any I/O.
- **Probes:**
  - `WaitForAdvisoryWaitersAsync`: pg_locks with the bigint key `(classid<<32)|objid`, `objsubid = 1`, not granted;
  - `WaitForLockWaitsAsync`: pg_stat_activity `Lock/<event>`;
  - both poll every 50 ms with a 15 s deadline. That poll is the only `Task.Delay` in the harness.
- **Raw helpers:** `InsertRegistryRowAsync` (parameterized, UTC-only, allowlisted table, owned DB only) and `BeginUncommittedTombstoneAsync`/`UncommittedInsert` (rolls back on dispose if not committed).
- **`LiveDatabase<TContext>`:**
  - production options: `UseNpgsql` + `EnableDynamicJson` + `AuditInterceptor`;
  - optional `IMigrator` pre-feature target, then legacy seed, then `MigrateAsync`, then `AfterMigrateAsync`, each on a fresh flag-off context;
  - drop in the `InitializeAsync` catch and in `DisposeAsync`;
  - a no-op when unconfigured.
- **Live self-tests** (`LivePostgresHarnessTests`, 4 tests):
  - prerequisites plus the single `LIVE-PG server_version=` output line;
  - create/UTF8/owner/drop;
  - advisory-waiter probe;
  - transactionid probe with a 23505 check after the holder commits.

## Task Commits

1. **Task 1 RED: failing guard tests** - `d356798e` (test)
2. **Task 1 GREEN: pure guards + guarded drop** - `47fc3f69` (feat)
3. **Task 2: lifecycle, probes, LiveDatabase, harness tests, Account link** - `84a96b01` (feat)

## Verification (env var removed with `env -u COFFEEPEEK_LIVE_PG`; presence was checked as `unset`)

- Guard class: `Total: 128, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0` (118 after Task 1, plus 10 Task 2 guard rows).
- Shops LivePostgres trait: `Total: 4, Errors: 0, Failed: 0, Skipped: 4, Not Run: 0`.
- Shops full: `Total: 196, Errors: 0, Failed: 0, Skipped: 4, Not Run: 0`. 192 passed = baseline 64 + 128.
- Account full: `Total: 89, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0`. The linked helper compiles; the project has no live tests yet.
- `dotnet test ... --filter "Category=LivePostgres"` (Shops, VSTest): `Skipped! - Failed: 0, Passed: 0, Skipped: 4, Total: 4`, exit 0.
- No compiler warnings from the new files. `Directory.Packages.props`, the Shops test csproj and `.github/workflows` are unchanged; no PackageReference was added.
- Nothing ran live.

## Decisions Made

See `key-decisions` in the frontmatter. The most important one for later plans: **constructor-level `Assert.SkipUnless` is confirmed to report Skipped** (research A1), under both runners. Plans 05-03..05-06 use the constructor pattern as written.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Security] Connect failures are rethrown without the host**
- **Found during:** Task 1 (the drop's server part)
- **Issue:** Npgsql connect errors (e.g. "Failed to connect to <host>") can contain the host. That breaks the "no host in output" truth whenever the server is unreachable (D-04 makes that a failing test with visible output).
- **Fix:** Public `LivePostgresServer.OpenAsync` rethrows as `InvalidOperationException` with a fixed message. The message names only exception types, SqlState and socket error codes, and has no inner exception. It is used for every connection the harness opens.
- **Files modified:** LivePostgresServer.cs, LivePostgresHarnessTests.cs
- **Commits:** 47fc3f69, 84a96b01

**2. [Rule 2 - Security] Ownership guard on probes and raw writes; 63-byte name limit**
- **Found during:** Task 2
- **Issue:** Only `DropDatabaseAsync` was specified to refuse non-owned databases. The raw insert, the tombstone blocker and the probes could otherwise run against any database. Separately, PostgreSQL silently truncates identifiers longer than 63 bytes, so a validated name and the name actually dropped could differ.
- **Fix:**
  - `InsertRegistryRowAsync`, `BeginUncommittedTombstoneAsync` and both probes refuse before connecting;
  - `IsOwnedDatabaseName` enforces a length of at most 63;
  - `NewDatabaseName` rejects services that would exceed it;
  - `ThrowawayConnectionString` always clears Password.
- **Tests:** these are covered by 10 additional server-free rows in `LivePostgresGuardTests`, plus `Context()` before initialization and a no-op `DisposeAsync`.
- **Files modified:** LivePostgresServer.cs, LivePostgresGuardTests.cs
- **Commit:** 84a96b01

**3. [Rule 1 - Bug] Drop-guard test assertion**
- **Found during:** Task 1 GREEN
- **Issue:** FluentAssertions `NotBeOfType` fails on a null InnerException. Also, once connect failures are wrapped (deviation 1), `BeOfType<InvalidOperationException>` alone no longer proves that the guard ran before connecting.
- **Fix:** The test asserts InnerException is null and that the message starts with the owned-name refusal text.
- **Commit:** 47fc3f69

**4. [Rule 2] Cleanup never hides the original failure**
- **Found during:** Task 2
- **Issue:** If the cleanup drop threw inside a catch, the original setup exception was lost.
- **Fix:** `DropAfterFailureAsync` throws an `AggregateException` of both exceptions if the drop fails. It is used by the post-create UTF8 check and by the `LiveDatabase` `InitializeAsync` catch.
- **Commit:** 84a96b01

### Minor notes

- **Drop order:** Task 1 already implemented the full `DropDatabaseAsync` (guard, `ClearAllPools`, `DROP ... WITH (FORCE)`), not a guard with a stub, so no stub was ever committed. Task 2 only added comments to it.
- **Fixture state timing:** `LiveDatabase` sets `ConnectionString`/`DatabaseName`/options right after creation, so `SeedLegacyAsync`/`AfterMigrateAsync` can use them (raw SQL seeding). It resets them in the failure catch.
- **Acceptance grep:** `grep -c 'Link="LivePostgres\\LivePostgresServer.cs"'` prints 0 on this machine's Git Bash GNU grep 3.0. That build does not match `\\` as a literal backslash; even `grep -c 'LivePostgres\\LivePostgresServer'` prints 0. The fixed-string equivalent `grep -cF 'Link="LivePostgres\LivePostgresServer.cs"'` prints 1, and the csproj line is exactly as specified. Later plans should use `grep -F` for backslash paths.

## Issues Encountered

None beyond the deviations above.

## User Setup Required

None for this plan. The first live run is plan 05-08: it needs the user's non-production attestation, `COFFEEPEEK_LIVE_PG` without a password, and pgpass or PGPASSFILE.

## Next Phase Readiness

- Plans 05-03..05-06 can consume the contract as written, plus the additive `LivePostgresServer.OpenAsync`.
- The harness self-tests have not executed against a server. The probes are trusted only after 05-08 (attested) or 05-07 reports `Failed: 0, Skipped: 0` for them.

## Self-Check: PASSED

- FOUND: CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresServer.cs
- FOUND: CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresGuardTests.cs
- FOUND: CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresHarnessTests.cs
- FOUND: CoffeePeek.Account.Infrastructure.Tests/CoffeePeek.Account.Infrastructure.Tests.csproj (Compile Link)
- FOUND: d356798e, 47fc3f69, 84a96b01
- TDD gate: test(05-01) d356798e precedes feat(05-01) 47fc3f69
