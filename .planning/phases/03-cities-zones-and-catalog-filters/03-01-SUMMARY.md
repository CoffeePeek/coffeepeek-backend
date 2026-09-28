---
phase: 03-cities-zones-and-catalog-filters
plan: 01
subsystem: database
tags: [slugs, public-addresses, ef-core, postgresql, migrations, cities, coffee-zones]

requires:
  - phase: 01-public-address-foundation
    provides: PublicSlug rules, PublicAddress base lifecycle, typed registry configuration
  - phase: 02-shop-roaster-public-addresses
    provides: Generic transactional coordinator (PublicAddressWriter/Service/Transaction), SaveChanges owner hook, audit table
provides:
  - CityPublicAddresses and CoffeeZonePublicAddresses typed registries with RESTRICT FKs to Cities/CoffeeZones
  - Generated migration 20260928082712_AddCityAndZonePublicAddresses (two tables, six indexes, CK_PublicAddressAudit_Kind widened to IN (1, 2, 3, 4))
  - PublicAddressKind.City = 3 and PublicAddressKind.CoffeeZone = 4
  - SaveChanges lifecycle hook for City create/delete and CoffeeZone first-publication/delete
  - Explicit four-kind dispatch in PublicAddressService and PublicAddressWriter.Create<T> (unknown kinds rejected, no default-to-Roaster)
affects: [03-02-public-and-admin-endpoints, public-addresses-runbook, live-postgresql-gate, external-frontend]

tech-stack:
  added: []
  patterns:
    - "Explicit kind switch with `_ => throw UnknownKind()` for every address operation"
    - "Zone allocation on first publication via EntityEntry Status IsModified (IsNewlyPublished)"
    - "Owner listing/stem/visibility always projected; CoffeeZone entities never materialized in the address service"

key-files:
  created:
    - CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CityPublicAddress.cs
    - CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CoffeeZonePublicAddress.cs
    - CoffeePeek.Shops.Persistance/Migrations/20260928082712_AddCityAndZonePublicAddresses.cs
    - CoffeePeek.Shops.Persistance/Migrations/20260928082712_AddCityAndZonePublicAddresses.Designer.cs
    - CoffeePeek.Shops.Infrastructure.Tests/Persistence/CityZonePublicAddressServiceTests.cs
  modified:
    - CoffeePeek.Shops.Application/PublicAddresses/IPublicAddressService.cs
    - CoffeePeek.Shops.Persistance/Configuration/PublicAddressConfiguration.cs
    - CoffeePeek.Shops.Persistance/Configuration/PublicAddressAuditConfiguration.cs
    - CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs
    - CoffeePeek.Shops.Persistance/Migrations/ShopsDbContextModelSnapshot.cs
    - CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressWriter.cs
    - CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs
    - CoffeePeek.Shops.Infrastructure.Tests/Persistence/PublicAddressPersistenceTests.cs
    - CoffeePeek.Shops.Domain.Tests/PublicAddresses/PublicAddressTests.cs
    - CoffeePeek.Shops.Domain.Tests/PublicAddresses/PublicSlugTests.cs

key-decisions:
  - "City and CoffeeZone use separate global per-type registries (D-01); canonical paths /cities/{slug} and /coffee-zones/{slug}; same-name zones in different cities get a deterministic suffix"
  - "Every City is public; a CoffeeZone is public only while Published, so Draft/Archived resolve like unknown slugs (D-02)"
  - "Zones allocate on first publication, not on create; Archive/MoveToDraft hide without retiring; republish and admin pre-initialized drafts keep their slug via idempotent InitializeAsync"
  - "City/Zone deletion always retires addresses, even when PublicAddresses is disabled (same as shops/roasters)"
  - "Admin owner lookup (GetStemAsync) has no zone status filter so admins can pre-initialize or inspect history of a Draft zone"

patterns-established:
  - "Four-arm kind switch: every PublicAddressService dispatch lists CoffeeShop, Roaster, City, CoffeeZone explicitly and throws 400 on anything else"
  - "Registry row factory: PublicAddressWriter.Create<T> maps each registry type explicitly and throws InvalidOperationException for unmapped types"

requirements-completed: [P3-01, P3-02, P3-03, P3-04, P3-06]

duration: 10min
completed: 2026-09-28
---

# Phase 3 Plan 01: City and Coffee Zone Public Address Registries Summary

**City and published-CoffeeZone public addresses on the phase 2 coordinator: two RESTRICT-FK registries plus a generated migration that widens the audit kind check, a SaveChanges hook for city create/delete and zone first-publication, and explicit four-kind dispatch everywhere (no silent default to Roaster).**

## Generated Migration

**Migration id: `20260928082712_AddCityAndZonePublicAddresses`** (plan 03-02 documents this in the enablement gate).

`Up` contains exactly:
- `DropCheckConstraint` + `AddCheckConstraint` for `CK_PublicAddressAudit_Kind` (`"Kind" IN (1, 2, 3, 4)`). `Down` restores `IN (1, 2)`.
- Two `CreateTable` calls: `CityPublicAddresses` (`FK_CityPublicAddresses_Cities_OwnerId`, Restrict) and `CoffeeZonePublicAddresses` (`FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId`, Restrict). Each has four checks (`_Slug`, `_Lifecycle`, `_Revision`, `_Timestamps`).
- Six `CreateIndex` calls: `IX_{table}_OwnerId` (unique, filtered `"State" = 1`), `IX_{table}_OwnerId_State`, `IX_{table}_Slug` (unique), once per table.
- No operations on any other table. The snapshot diff contains only the two entities, their FKs and the widened audit check. `has-pending-model-changes` reports a clean model.

Generated with `PostgresCpOptions__ConnectionString="Host=localhost;Database=unused;Username=u;Password=p" make mig-shops n=AddCityAndZonePublicAddresses`. No database connection was made. Earlier migrations were not edited.

## Performance

- **Duration:** about 10 min
- **Started:** 2026-09-28T08:25:21Z
- **Completed:** 2026-09-28T08:35:00Z
- **Tasks:** 2 of 2 (TDD: 4 commits)
- **Files modified:** 15 (5 created, 10 modified)

## Accomplishments

- Added `CityPublicAddress` and `CoffeeZonePublicAddress`, clones of `CoffeeShopPublicAddress`. `PublicAddressKind` now has `City = 3, CoffeeZone = 4`, with a comment that the values are persisted and must never be renumbered.
- `CityPublicAddressConfiguration` and `CoffeeZonePublicAddressConfiguration` reuse the unchanged generic `Configure<T>`. Its unfiltered unique slug index per table provides the D-01 global per-type namespace.
- `PublicAddressWriter.PrepareOwnerChangesAsync` now handles cities and zones:
  - A deleted City or CoffeeZone always retires its addresses, even when the feature is disabled.
  - An added City allocates from `City.Name`.
  - A zone allocates only when `IsNewlyPublished`: added as Published, or `Status` modified to Published.
  - A City display-name edit, a Draft create, Archive and MoveToDraft do nothing.
- The `ShopsDbContext` `ownerChanges` gate includes City and CoffeeZone deletes, City adds and newly published zones.
- In `PublicAddressService`, every binary ternary is now a four-arm switch. That covers Resolve, GetByIds, Change, Backfill, the new `VisibleIdsAsync` and `ListOwnersAsync`, GetStem, IsVisible and the canonical path segment. Unknown kinds return 400. `IsAddressConflict` maps `IX_CityPublicAddresses_*` and `IX_CoffeeZonePublicAddresses_*` unique violations to 409. The shop and roaster arms keep their earlier queries unchanged.
- Model tests pin both new tables: shape, collation, concurrency token, indexes, checks and FK principal (City/CoffeeZone). They also assert that the audit kind check SQL equals `"Kind" IN (1, 2, 3, 4)` and matches `PublicAddressKind` exactly.
- Eight InMemory tests cover:
  - City lifecycle: create, display rename, A->B->A, delete, reallocation with a suffix.
  - Zone publish/archive/draft/republish.
  - An admin pre-initialized draft zone.
  - Per-kind namespaces.
  - City and zone backfill: dry-run, cursor, replay, published zones only.
  - Disabled mode.
  - Delete while disabled.
  - Unknown owners and kinds.

## Task Commits

1. **Task 1: Registries, audit kind check widening, generated migration, model tests**
   - RED `33f94a94`: `test(03-01): add failing city/zone registry model and domain tests`
   - GREEN `f04e325f`: `feat(03-01): add city and coffee zone public address registries`
2. **Task 2: Lifecycle hook, four-kind service dispatch, InMemory regression tests**
   - RED `8233c3f1`: `test(03-01): add failing city/zone public address lifecycle tests`
   - GREEN `3566bf48`: `feat(03-01): allocate city and published zone addresses with explicit kind dispatch`

No refactor commits were needed. No packages were added. No tracked file was deleted.

## Verification

| Check | Result |
|---|---|
| `CoffeePeek.Shops.Infrastructure.Tests` (full) | 64 passed, 0 failed (baseline 53; +2 theory rows, +1 audit fact, +8 service tests) |
| `--filter FullyQualifiedName~PublicAddress` | 17 passed (8 new + 4 existing shop/roaster + 5 persistence cases) |
| `CoffeePeek.Shops.Domain.Tests` (full) | 189 passed (baseline 173; +14 slug rows, +2 lifecycle rows) |
| `CoffeePeek.Shops.Application.Tests` | 224 passed (baseline 224) |
| `CoffeePeek.ShopsService.Tests` (Wolverine pre-generated handlers) | 11 passed (baseline 11) |
| `has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| `migrations script 20260928072406_AddPublicAddressAudit AddCityAndZonePublicAddresses` | Contains both `CREATE TABLE`s, `REFERENCES "Cities" ("Id") ON DELETE RESTRICT`, `REFERENCES "CoffeeZones" ("Id") ON DELETE RESTRICT`, `DROP CONSTRAINT "CK_PublicAddressAudit_Kind"`, `CHECK ("Kind" IN (1, 2, 3, 4))` |
| Migration forbidden ops (`AlterColumn`, `DropIndex`, and so on) | 0 |
| `kind == PublicAddressKind` in service | 0; `PublicAddressKind.City =>` 9, `PublicAddressKind.CoffeeZone =>` 9 |
| Unchanged since `d812e0c0` | Earlier migrations, `PublicSlug.cs`, `CoffeePeek.Contract`, `Application/Features`, `ShopsService/Internal/Generated`, `PublicAddressServiceTests.cs` |
| `git diff --check` | clean |

**Not verified (no live PostgreSQL or Docker available):** these results come from InMemory tests and Npgsql design-time model/SQL checks only. Nothing here proves, against a live database:
- Slug uniqueness under concurrency
- RESTRICT FK enforcement, including City delete while zones exist returning 409 with the retirement rolled back
- That the check constraints accept kinds 3 and 4
- Savepoint rollback
- Advisory-lock serialization

These stay live PostgreSQL gate items. `PublicAddresses__Enabled` still defaults to false.

## Decisions Made

- Followed D-01..D-03 as locked (see key-decisions).
- Added a small `UnknownKind()` factory in `PublicAddressService` so every switch arm and `RequireEnabled` share the same message and status (400). Messages and status codes are unchanged.
- `GetStemAsync` is an async switch expression. The CoffeeShop arm keeps the earlier behavior: it loads the shop and calls `PublicAddressWriter.ShopStem`. The other arms project `Name`.

## Deviations from Plan

None in behavior. Two clerical notes:
- The Task 2 behavior block lists eight tests, including `UnknownOwnersAndKinds_AreRejected`. The acceptance text says "all 7". All eight were implemented and pass.
- The `IsNewlyPublished` zone filter is written as a lambda (`zones.Where(e => IsNewlyPublished(e))`), which does the same thing as the plan's wording.

## TDD Gate Compliance

- Task 1: RED `33f94a94` failed at compile time because the new registry types were missing. GREEN `f04e325f` passes.
- Task 2: in RED `8233c3f1`, 6 of 8 tests failed. The two that passed before implementation were:
  - `Disabled_NoAllocationAndServiceReturns503`: disabled mode was already correct because of the `RequireEnabled` guard and the disabled-mode gate.
  - `UnknownOwnersAndKinds_AreRejected`: `Enum.IsDefined` already returned 400, and the old fallback to the Shops lookup already produced 404.

  They are kept as regression locks for the new dispatch. GREEN `3566bf48` passes all 8.

## Issues Encountered

None. The generated migration matched the expected shape on the first run, so no regeneration was needed.

## Next Phase Readiness

- Plan 03-02 can add the public `api/Catalogs/cities` and `api/Catalogs/coffee-zones` endpoints (by-slug, batch, `{id}/public-address`). It can also extend the admin route regex and `Kind()` switch to `cities|coffee-zones`, and document migration `20260928082712_AddCityAndZonePublicAddresses` in the enablement gate.
- Live PostgreSQL gate items (listed above) must be added to `docs/api/public-addresses.md` in 03-02.

## Self-Check: PASSED

- FOUND: CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CityPublicAddress.cs
- FOUND: CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CoffeeZonePublicAddress.cs
- FOUND: CoffeePeek.Shops.Persistance/Migrations/20260928082712_AddCityAndZonePublicAddresses.cs
- FOUND: CoffeePeek.Shops.Persistance/Migrations/20260928082712_AddCityAndZonePublicAddresses.Designer.cs
- FOUND: CoffeePeek.Shops.Infrastructure.Tests/Persistence/CityZonePublicAddressServiceTests.cs
- FOUND commits: 33f94a94, f04e325f, 8233c3f1, 3566bf48
