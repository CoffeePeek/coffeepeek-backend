---
phase: 04-public-profiles-in-account
plan: 01
subsystem: database
tags: [slugs, public-addresses, ef-core, postgresql, migrations, account, user-profiles]

requires:
  - phase: 01-public-address-foundation
    provides: PublicSlug rules, PublicAddress base lifecycle, typed registry schema pattern
  - phase: 02-shop-roaster-public-addresses
    provides: Transaction helper (savepoint/owned tx + advisory lock) and SaveChanges owner hook pattern, copied Account-locally
provides:
  - UserPublicAddresses single-kind registry in cpaccountdb with a RESTRICT FK to Users and no navigation
  - Generated migration 20260928134755_AddUserPublicAddresses (one table, three indexes, four checks)
  - AccountDbContext SaveChanges hook: always retire on soft/hard delete; allocate on create and follow username changes (Policy A) while enabled
  - ProfileAddressWriter owner-aware candidate search (own canonical = no-op, own alias = restore) and user-<8hex> fallback stem
  - PublicAddressOptions (section PublicAddresses, default false) bound in Account.Persistence DI
affects: [04-02-profile-address-service-and-endpoints, 04-03-gateway-docs-verification, public-addresses-runbook, live-postgresql-gate]

tech-stack:
  added: []
  patterns:
    - "Change detection compares OriginalValue with the current value; never IsModified (DbSet.Update marks every property)"
    - "Writer flushes and saves only through AccountDbContext.SaveCoreAsync (base save), so the hook never re-enters"
    - "Owner-aware candidate search returns (slug, row): free slug, own canonical (no-op) or own alias (restore)"
    - "Optional IOptions<T> DbContext ctor parameter (singleton only; safe with Aspire pooling)"

key-files:
  created:
    - CoffeePeek.Account.Domain/Entities/PublicAddressAggregate/UserPublicAddress.cs
    - CoffeePeek.Account.Application/Common/Options/PublicAddressOptions.cs
    - CoffeePeek.Account.Persistence/Configuration/UserPublicAddressConfiguration.cs
    - CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressTransaction.cs
    - CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs
    - CoffeePeek.Account.Persistence/Migrations/20260928134755_AddUserPublicAddresses.cs
    - CoffeePeek.Account.Persistence/Migrations/20260928134755_AddUserPublicAddresses.Designer.cs
    - CoffeePeek.Account.Domain.Tests/UserPublicAddressTests.cs
    - CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressPersistenceTests.cs
    - CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressLifecycleTests.cs
  modified:
    - CoffeePeek.Account.Persistence/Configuration/AccountDbContext.cs
    - CoffeePeek.Account.Persistence/Migrations/AccountDbContextModelSnapshot.cs
    - CoffeePeek.Account.Persistence/DependencyInjection.cs

key-decisions:
  - "Account-local single-kind registry: UserPublicAddress + UserPublicAddressConfiguration copy the Shops schema verbatim and add a RESTRICT FK to Users; model tests pin exact check SQL parity (D-05)"
  - "Retirement runs before the enabled check, so soft delete (confirmation, admin delete) and hard delete always tombstone every row, flag on or off (D-03); block/unblock never touches addresses"
  - "Username change is an owner-authorized rename (Policy A, D-01): old canonical becomes an alias, own alias is restored (A->B->A keeps row ids), same-slug changes are no-ops including an owner's own suffixed slug"
  - "Reserved, unsupported-script and bare 'user' usernames always get user-<8hex>; no person owns a bare place or user address"
  - "Advisory lock key 7402100401 (Account-specific; locks are per database)"
  - "A pre-backfill user created while disabled gets an address on the first enabled username change"

patterns-established:
  - "Account hook gate: IsRetiring(e) || (PublicAddressesEnabled && NeedsAddress(e)); entries classified synchronously before any await"
  - "Lifecycle tests: fresh AccountDbContext per request on one InMemoryDatabaseRoot, real UserRepository + UnitOfWork, precondition asserted before each action"

requirements-completed: [D-01, D-03, D-05, D-06, P4-01, P4-02, P4-04, P4-06]

duration: 18min
completed: 2026-09-28
---

# Phase 4 Plan 01: User Profile Address Registry and Lifecycle Summary

**An Account-local `UserPublicAddresses` registry. It has a generated migration and a RESTRICT FK to Users, and it is schema-identical to the Shops registries. An `AccountDbContext` SaveChanges hook always retires addresses on deletion. While the flag is on, the hook also allocates an address at user creation and follows username changes with aliases. Detection compares original with current values, the owner-aware candidate search prevents a user colliding with their own slug, and saves never re-enter the hook.**

## Generated Migration

**Migration id: `20260928134755_AddUserPublicAddresses`.** Plan 04-03 documents it as a deployment prerequisite that applies whatever the flag says, because account deletion queries the table even when the feature is disabled.

`Up` contains exactly:
- One `CreateTable("UserPublicAddresses")` with 7 columns. `Slug` is `character varying(100)` with `COLLATE "C"`. The table has the four checks `CK_UserPublicAddresses_Slug/_Lifecycle/_Revision/_Timestamps`, with SQL identical to Shops, and `FK_UserPublicAddresses_Users_OwnerId` with `ReferentialAction.Restrict`.
- Three `CreateIndex` calls:
  - `IX_UserPublicAddresses_OwnerId`: unique, filter `"State" = 1`.
  - `IX_UserPublicAddresses_OwnerId_State`.
  - `IX_UserPublicAddresses_Slug`: unique.

`Down` contains only `DropTable`. No other table is touched: `AlterColumn`/`AddColumn`/`DropIndex`/... count is 0, and there is no audit table or cooldown column (D-06, D-02). The snapshot diff adds only this entity and its FK.

I generated it with `PostgresCpOptions__ConnectionString="Host=localhost;Database=unused;Username=u;Password=p" make mig-acc n=AddUserPublicAddresses`. No database connection was made, and no earlier migration was edited.

## Performance

- **Duration:** about 18 min
- **Started:** 2026-09-28T13:41:00Z
- **Completed:** 2026-09-28T13:59:00Z
- **Tasks:** 2 of 2 (TDD: 4 commits)
- **Files:** 13 (10 created, 3 modified)

## Accomplishments

- **Registry row.** `UserPublicAddress` is an exact clone of `CoffeeShopPublicAddress`, with only the 7 base columns and no navigation. A tombstone therefore keeps only the reserved slug.
- **Schema.** `UserPublicAddressConfiguration` reproduces the Shops `Configure<T>` statements, including `HasBaseType(null)`, and adds `HasOne<User>().WithMany()` with Restrict. `ApplyConfigurationsFromAssembly` picks it up.
- **Hook.** `AccountDbContext` has an optional `IOptions<PublicAddressOptions>?` constructor parameter, so `AccountDbContextFactory`, the existing tests and Aspire pooling keep working. It also has `PublicAddressesEnabled`, sync and async `SaveChanges` overrides and `internal SaveCoreAsync`. Generated Wolverine handlers resolve the context through service location, so there is no codegen drift.
- **Writer.** `ProfileAddressWriter`:
  - `IsRetiring`: the user is `Deleted`, or its `IsSoftDelete` goes from false to true, based on `OriginalValue`.
  - `NeedsAddress`: the user is not soft-deleted and is either `Added`, or `Modified` with the original `Username` different from the current one.
  - `Stem` falls back to `user` and starts at attempt 1.
  - `FindCandidateAsync` is owner-aware and throws `ConflictException` (409) after 6 attempts.
  - `SaveAsync` retires first (always), returns early when disabled, then initializes and follows renames.
  - `FollowUsernameAsync` makes the current canonical an alias, flushes through `SaveCoreAsync(true)`, then restores the own alias or adds a new canonical.
  - No audit rows.
- **Transaction.** `ProfileAddressTransaction` is a line-for-line copy of the Shops helper: InMemory skip, savepoint or owned transaction, rollback with `CancellationToken.None`, then `ChangeTracker.Clear()`. Only the lock key differs: `pg_advisory_xact_lock(7402100401)`.
- **DI.** `services.Configure<PublicAddressOptions>(builder.Configuration.GetSection("PublicAddresses"))` is the first statement of `AddPersistence`. No appsettings, compose or AppHost value was added, so the flag stays unset and false.

## Tests

- `UserPublicAddressTests` (domain, 24 cases):
  - Lifecycle: canonical, then alias, then restore, then retire, ending as a Tombstone with OwnerId null, the slug kept and Revision 4; Restore and Retire on the tombstone then throw.
  - Create validation: empty owner, reserved slug, GUID-shaped slug, revision 0 and local time all throw.
  - The 22-row `Generate_UsernameRegression` theory. All rows match the research spike; `PublicSlug` is unchanged.
- `UserPublicAddressPersistenceTests` (Npgsql design-time model, 7 facts):
  - Table name and no base type.
  - Exactly the 7 properties, with no navigations or skip navigations.
  - Slug max length 100, collation `C`, not nullable; Revision is a concurrency token.
  - Exactly 3 indexes: unfiltered unique Slug, unique OwnerId filtered on `"State" = 1`, non-unique (OwnerId, State).
  - A single Restrict FK to `User` with both navigations null.
  - The four check constraints, each asserted by exact SQL string equality against literal Shops copies.
  - `User` has no navigation to the registry.
- `UserPublicAddressLifecycleTests` (InMemory, 11 cases, through the real `UserRepository` and `UnitOfWork<AccountDbContext>`): all ten behaviors from the plan, including the enabled/disabled deletion theory. Every test asserts its precondition before the action under test, so none can pass vacuously (03-REVIEW WR-01/IN-03).

**Mutation checks** were temporary edits, each reverted:

| Mutation | Tests that caught it |
|---|---|
| `IsModified`-based username detection | `UnrelatedUpdates_NeverRenameAllocateOrRetire` |
| Disabled check moved before retirement | `Deletion_...(False)` and `HardDelete_...` |
| Candidate search not owner-aware | `Rename_...AtoBtoA` and `Rename_CaseOrPunctuationOnly_...` |
| Demotion flush through the overridden `SaveChangesAsync` (re-entry) | 4 tests |

## Task Commits

1. **Task 1: Registry, schema configuration, generated migration, model and domain tests**
   - RED `5adff179`: `test(04-01): add failing registry model and user slug domain tests`
   - GREEN `7c92a725`: `feat(04-01): add UserPublicAddress registry and generated migration`
2. **Task 2: SaveChanges lifecycle hook with InMemory regression tests**
   - RED `2f43ea54`: `test(04-01): add failing profile address lifecycle tests`
   - GREEN `824457f1`: `feat(04-01): add profile address SaveChanges lifecycle hook`

No refactor commits were needed. No packages were added and no tracked file was deleted.

## Verification

| Check | Result |
|---|---|
| `CoffeePeek.Account.Infrastructure.Tests` (full) | 72 passed, 0 failed (baseline 54; +7 model facts, +11 lifecycle cases), including `WolverinePreGeneratedHandlersTests` |
| `--filter FullyQualifiedName~UserPublicAddress` | 18 passed |
| `CoffeePeek.Account.Domain.Tests` (full) | 255 passed (baseline 231; +24) |
| `CoffeePeek.Account.Application.Tests` | 214 passed (baseline 214) |
| `dotnet build CoffeePeek.Backend.ci.slnf -c Release` | 0 errors; no warnings from new or changed files |
| `has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| `migrations script 20260921104154_AddAccountDeletionConfirmationSecurity AddUserPublicAddresses` | Contains `CREATE TABLE "UserPublicAddresses"`, `COLLATE "C"`, `REFERENCES "Users" ("Id") ON DELETE RESTRICT`, `CREATE UNIQUE INDEX "IX_UserPublicAddresses_Slug"`, `WHERE "State" = 1` |
| Migration gates | `CreateTable(` 1, `DropTable(` 1, forbidden ops 0 |
| Writer grep gates | Non-comment `IsModified` 0, `OriginalValue` 2; non-comment `SaveChangesAsync` 0, `SaveCoreAsync` 3 |
| Transaction grep gates | `pg_advisory_xact_lock(7402100401)` 1, `ChangeTracker.Clear()` 1 |
| `GetSection("PublicAddresses")` in Account DI | 1 |
| Audit class or DbSet in Account | none (D-06) |
| `PublicAddresses` flag in AccountService appsettings, deploy, AppHost | none (unset, default false) |
| Unchanged since `96acf741` | Earlier Account migrations, `CoffeePeek.Shops.*`, `ShopsService`, `Shared.Domain`, `Shared.Persistence`, `Contract`, `AccountService/Internal/Generated`, `Account.Application/Features`, `Account.Persistence/Repositories`, `Account.Domain/Entities/UserAggregate` |
| `git diff --check` | clean |

**Not verified (no live PostgreSQL or Docker):** these results come from InMemory behavior tests and Npgsql design-time model and SQL checks only. Nothing here proves any of the following against a live database:
- Slug uniqueness or the filtered canonical owner index under concurrent registrations (`Ivan`/`ivan`/`Иван` at the same time)
- Demotion-flush ordering against `IX_UserPublicAddresses_OwnerId`
- `FK_UserPublicAddresses_Users_OwnerId` RESTRICT enforcement
- The four check constraints
- Savepoint rollback inside Wolverine's ambient transaction: a username unique violation leaving the address unchanged, a duplicate-email registration leaving no row, and the Google conflict retry after `ChangeTracker.Clear()`
- The advisory lock
- The migration applying cleanly to `cpaccountdb`

These stay live-gate items for plan 04-03. `PublicAddresses__Enabled` stays unset and false.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] `Options.Create` resolved to the `CoffeePeek.Account.Infrastructure.Options` namespace in the test project**
- **Found during:** Task 2 RED compile
- **Issue:** Inside namespace `CoffeePeek.Account.Infrastructure.Tests.Persistence`, `Options` bound to an existing Account namespace, not `Microsoft.Extensions.Options.Options`.
- **Fix:** Added the alias `using MsOptions = Microsoft.Extensions.Options.Options;` in `UserPublicAddressLifecycleTests.cs`.
- **Commit:** `2f43ea54`

**2. [Rule 2 - Correctness] `FindCanonicalAsync` ignores a tracked row that is no longer canonical**
- **Found during:** Task 2 implementation
- **Issue:** When the Local view has no canonical row, the database query can still return, through identity resolution, a tracked instance that was demoted or retired in the same save but not yet flushed.
- **Fix:** The query result is returned only if its current `State` is Canonical. No current path reaches this case; the guard keeps the lookup correct if one does.
- **Files:** `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs`
- **Commit:** `824457f1`

### Minor execution notes (no behavior impact)

- **Test helpers.** They use a per-test `InMemoryDatabaseRoot` and database name in instance fields; xUnit creates a new instance per test and per theory row. They wrap the static `Db(root, name, enabled)` factory rather than passing the root and name to every helper. Isolation is the same as planned.
- **RED evidence for Task 2.** The RED commit contains only the test file. To show behavioral failure rather than just a compile error, I added `PublicAddressOptions` and the optional constructor parameter before the RED run: all 11 cases failed. Those two changes were committed in GREEN (`824457f1`).
- **Extra assertions.** The lifecycle tests also assert that the username change itself persisted (`UsernameAsync`), and that the hard-deleted user row is gone.

## TDD Gate Compliance

For both tasks the RED `test(04-01)` commit precedes the GREEN `feat(04-01)` commit: `5adff179` then `7c92a725`, and `2f43ea54` then `824457f1`. Task 1 RED was a compile failure, because the type was missing. Task 2 RED was 11 of 11 behavioral failures.

## Known Stubs

None. This plan intentionally adds no lookup service or HTTP surface; plan 04-02 adds those.

## Next Phase Readiness

- Plan 04-02 can add `ProfileAddressService`, using `ProfileAddressWriter.FindCandidateAsync` for backfill, the public `api/Users` endpoints, the admin backfill and the GUID-route 404 fix.
- Plan 04-03 must document `20260928134755_AddUserPublicAddresses` as a deployment prerequisite that applies whatever the flag says, together with the `user-<8hex>` fallback and the live gate items above.

## Self-Check: PASSED

- All 10 created files exist on disk.
- Commits `5adff179`, `7c92a725`, `2f43ea54` and `824457f1` are present in `git log`.
