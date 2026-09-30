---
phase: 05-operational-backfill-compatibility-and-rollout-verification
plan: 02
subsystem: shops-persistence, docs
tags: [public-addresses, backfill, 409, ef-core, npgsql, runbook, live-postgres]

# Dependency graph
requires:
  - phase: 05-01
    provides: LivePostgresServer env/guard contract (COFFEEPEEK_LIVE_PG, cp_live_* names, lock_timeout, LIVE-PG server_version= line) documented by the runbook
provides:
  - Shops write-mode backfill maps in-flight address unique violations to PublicAddressException 409 (D-05 parity with Account)
  - 8 server-free regression rows for the mapping, its filter and dry-run no-save
  - docs/api/public-addresses.md "Live PostgreSQL gate" runbook (D-02, D-03, D-04), "Dry-run backfill review" loop (D-07), manual HTTP smoke (D-06), rollback rehearsal, D-09/D-10 not covered
affects: [05-04, 05-06, 05-07, 05-08]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Server-free DB error simulation: SaveChangesInterceptor.SavingChangesAsync throws DbUpdateException(new PostgresException(..., sqlState, constraintName: ...)) on an InMemory context"
    - "Backfill conflict mapping: async wrapper with catch (DbUpdateException ex) when (IsAddressConflict(ex)) around the owned transaction"

key-files:
  created:
    - CoffeePeek.Shops.Infrastructure.Tests/Persistence/PublicAddressBackfillConflictTests.cs
  modified:
    - CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs
    - docs/api/public-addresses.md

key-decisions:
  - "Shops BackfillAsync reuses the existing IsAddressConflict filter and the exact ChangeAsync message/status (409 'Address is reserved or changed concurrently.'); IPublicAddressService, PublicAddressException, controllers and Contract DTOs are unchanged"
  - "The runbook loop pauses for an explicit y/N confirmation before each write and stops on any HTTP error, 409 or dry-run/write diff; CURSOR stays on the stopped page so a re-run resumes there, dry run first"
  - "Live gate evidence holds exactly 'No green run has been recorded yet.' plus the flag-unset sentence; D-08 evidence is written only by 05-07/05-08"

patterns-established:
  - "Runbook placeholders only: <host>, <secret>, <client-ip>, $GW, $TOKEN (read with read -rs); share only summary lines, LIVE-PG server_version= and the short commit"

requirements-completed: [D-05, D-06, D-07, D-09, D-10, G-S10b, G-M1, G-M2]

# Metrics
duration: 11min
completed: 2026-09-29
---

# Phase 5 Plan 02: Shops backfill 409 parity and operator runbook Summary

**Shops write-mode backfill now turns an in-flight registry or audit unique violation into the same 409 that `ChangeAsync` and Account use, pinned by 8 server-free rows (red before the fix). `docs/api/public-addresses.md` now has the live PostgreSQL gate runbook, a page-by-page dry-run backfill loop, a manual HTTP smoke and a rollback rehearsal. It still claims no green live run.**

## Performance

- **Duration:** about 11 min
- **Started:** 2026-09-29T13:24Z (right after 05-01)
- **Completed:** 2026-09-29T13:35Z
- **Tasks:** 2
- **Files modified:** 3 (1 created, 2 modified)

## Accomplishments

- **D-05 fix (`PublicAddressService.BackfillAsync`):**
  - The method is now `async` and wraps `dryRun ? await Execute() : await PublicAddressTransaction.RunAsync(db, Execute, ct)` in a `try`.
  - `catch (DbUpdateException ex) when (IsAddressConflict(ex))` throws `PublicAddressException("Address is reserved or changed concurrently.", 409)`.
  - A one-line comment ties the catch to G-S10b.
  - Validation, `RequireEnabled`, the kind switch and `IsAddressConflict` are unchanged. `grep -c` finds the catch twice (ChangeAsync and BackfillAsync).
- **Tests (`PublicAddressBackfillConflictTests`, 8 rows):**
  - The 409 Theory covers `IX_CoffeeShopPublicAddresses_Slug`, `IX_RoasterPublicAddresses_OwnerId`, `IX_CityPublicAddresses_Slug`, `IX_CoffeeZonePublicAddresses_Slug` and `IX_PublicAddressAudit_Kind_OwnerId_Revision`. It checks the exact type, the status and the message.
  - The negative Theory shows that 23505 on `IX_Shops_ModerationId` and 23503 on `FK_CityPublicAddresses_Cities_OwnerId` still escape as a plain `DbUpdateException`, with the inner SqlState preserved.
  - `DryRunBackfill_NeverSaves`: on a context where every save throws, a dry run still returns one item that is not yet initialized, has a slug and has a null cursor.
  - Setup: owners are seeded through a flag-off context on a shared InMemory root. The service runs on a second, enabled context with a throwing `SaveChangesInterceptor`.
- **Runbook (`docs/api/public-addresses.md`):**
  - Enablement gate:
    - Step 2 points to [Live PostgreSQL gate](#live-postgresql-gate).
    - Both "none of them has been verified" sentences now say the items are automated and pending until [Live gate evidence](#live-gate-evidence) records a green run.
    - The HTTP smoke bullet points to the manual section.
    - Step 4 links the dry-run review.
  - New `## Live PostgreSQL gate` section:
    - a coverage table mapping each gate item to a test class and gate IDs;
    - server and role requirements (PG 17+, `pg_trgm`, UTF8, the `CREATE ROLE` SQL, the `pg_hba.conf` line, no production role membership);
    - credentials (D-02);
    - safety guards (D-03);
    - run commands for the in-process runner and for `dotnet test`, and which lines to share;
    - the pass rule (D-04: `Failed: 0`, `Skipped: 0`, `Errors: 0`, and the passed count equals the unset-skip count);
    - cleanup SQL;
    - what is not covered (D-09, D-10, the HTTP smoke, the step 5 decisions);
    - `### HTTP smoke (manual)` (D-06);
    - `### Live gate evidence`.
  - New `## Dry-run backfill review` section (D-07):
    - the bash/curl/jq loop, run page by page;
    - the review checklist;
    - "Why page by page" (the cross-page caveat);
    - the replay check (`jq -e 'all(.items[]; .alreadyInitialized)'`);
    - retrying the same page after a 409;
    - the AccountService variant, with no manual initialize.
  - New `### Rollback rehearsal` section: flag off and restart, 503 on the new endpoints, GUID routes unchanged, deletions keep retiring, re-enabling resolves again, never run `Down`. It notes that G-S11 and G-A9 automate the data-level part and are still pending.

## Task Commits

1. **Task 1 RED: failing backfill conflict tests** - `6386fce5` (test)
2. **Task 1 GREEN: 409 mapping in BackfillAsync** - `25b6e4ec` (fix)
3. **Task 2: runbook** - `6073c62d` (docs)

## Verification (env var removed with `env -u COFFEEPEEK_LIVE_PG`; presence checked as unset, value never printed)

- RED, before the fix: `PublicAddressBackfillConflictTests` `Total: 8, Errors: 0, Failed: 5, Skipped: 0`. The 5 failures are the 409 Theory rows ("found DbUpdateException"). The negative and dry-run tests already passed; they describe behavior that must not change.
- GREEN: `Total: 8, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0`.
- Shops.Infrastructure.Tests: `Total: 204, Errors: 0, Failed: 0, Skipped: 4, Not Run: 0`. The 200 passed are the 05-01 baseline of 192 plus these 8. The 4 skips are the live harness self-tests.
- Shops.Application.Tests: `Total: 224, Errors: 0, Failed: 0, Skipped: 0`.
- ShopsService.Tests: `Total: 25, Errors: 0, Failed: 0, Skipped: 0`.
- Account.Infrastructure.Tests: `Total: 89, Errors: 0, Failed: 0, Skipped: 0`, checked as a sanity check.
- LivePostgres trait with the var unset: `Total: 4, Skipped: 4`, which is the reference count the pass rule uses.
- The runner switches `-trait`, `-showLiveOutput` and `-noColor` are confirmed by `-?`. The documented `dotnet build … -c Release` builds without `AllowMissingPrunePackageData` on this machine.
- Nothing changed outside the three plan files:
  - `git diff --name-only 17521f71 -- CoffeePeek.Shops.Application CoffeePeek.Contract CoffeePeek.ShopsService CoffeePeek.Shops.Persistance/Migrations` is empty;
  - `git diff --name-only 17521f71 -- deploy dev .github` is empty;
  - `git diff --name-only 84a96b01 HEAD` lists exactly the three plan files.
- Doc checks:
  - all 37 acceptance tokens are present (`grep -F`);
  - `none of them has been verified` occurs 0 times;
  - the `PublicAddresses(__|:|")` grep over appsettings, deploy and AppHost finds nothing;
  - `git diff --check` is clean;
  - every in-page anchor has exactly one target heading;
  - there are no IPs, JWTs or passwords, only placeholders.
- Nothing ran live.

## Decisions Made

See `key-decisions` in the frontmatter.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Integrity, T-05-09] The backfill loop pauses before writing and stops on failure**
- **Found during:** Task 2
- **Issue:** The research loop wrote each page right after printing the jq review, with no chance to stop and initialize a slug by hand. It also continued after an HTTP error: a 409 body would be diffed and the cursor read from an error file.
- **Fix:**
  - `read -rp "Write page $P? [y/N]"` before each write;
  - `curl --fail-with-body … || break` on both POSTs;
  - `|| break` on the diff;
  - the cursor is initialized outside the loop, so a stopped loop resumes at the same page, dry run first.
  - The doc also says to read the token with `read -rs TOKEN` (T-05-10).
- **Files modified:** docs/api/public-addresses.md
- **Commit:** 6073c62d

### Minor notes

- The negative Theory takes a `kind` column: CoffeeShop for `IX_Shops_ModerationId` and City for the FK case. The two SqlState/constraint pairs are as the plan lists them.
- The dry-run fact also asserts `DryRun == true` and `NextCursor == null` (one owner, limit 100).
- **Forward references in the runbook:**
  - The coverage table names `ShopsLive*`, `ShopsBackfillRehearsalTests` and `AccountLive*`/`AccountBackfillRehearsalTests`. The plan fixes these names, but plans 05-03..05-06 add the classes; only `LivePostgresHarnessTests` exists at `6073c62d`.
  - The pass rule compares against the unset-skip count, so it stays correct as those classes land.
  - The harness `SkipReason` link `docs/api/public-addresses.md#live-postgresql-gate` now resolves.

## TDD Gate Compliance

- RED `test(05-02)` `6386fce5` precedes GREEN `25b6e4ec`.
- The GREEN commit is typed `fix(05-02)`, not `feat(05-02)`, because the change fixes a bug: a 500 becomes a 409. No REFACTOR commit was needed.

## Issues Encountered

None.

## User Setup Required

None for this plan. The runbook lists what the user must provide for the live run (05-07/05-08): a non-production PG 17+ server, the `cp_live_tests` role, `COFFEEPEEK_LIVE_PG` without a password, and pgpass or `PGPASSFILE`.

## Next Phase Readiness

- Plan 05-04 (G-S10b) can assert `PublicAddressException` 409 for a Shops in-flight backfill conflict and check that the page was rolled back on real SQL.
- Plan 05-07 records evidence under `### Live gate evidence` (D-08) only after both projects report `Failed: 0, Skipped: 0`.

## Self-Check: PASSED

- FOUND: CoffeePeek.Shops.Infrastructure.Tests/Persistence/PublicAddressBackfillConflictTests.cs
- FOUND: CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs (2 catch sites)
- FOUND: docs/api/public-addresses.md (`## Live PostgreSQL gate`, `## Dry-run backfill review`, `### Rollback rehearsal`)
- FOUND: 6386fce5, 25b6e4ec, 6073c62d
