---
phase: 03-cities-zones-and-catalog-filters
plan: 02
subsystem: api
tags: [slugs, public-addresses, controllers, admin, ci, docs, cities, coffee-zones]

requires:
  - phase: 03-cities-zones-and-catalog-filters
    plan: 01
    provides: City/CoffeeZone registries, PublicAddressKind.City/CoffeeZone, four-kind service dispatch, migration 20260928082712_AddCityAndZonePublicAddresses
  - phase: 02-shop-roaster-public-addresses
    provides: PublicRoasterAddressesController pattern, AdminPublicAddressesController, PublicAddressApiContractTests
provides:
  - GET api/Catalogs/cities/{by-slug/{slug}|public-addresses|{id:guid}/public-address} returning PublicAddressEnvelope<CityDto>
  - GET api/Catalogs/coffee-zones/{by-slug/{slug}|public-addresses|{id:guid}/public-address} returning PublicAddressEnvelope<MapCoffeeZoneDto>, Published only with re-check on the data read
  - Admin address management for cities and coffee-zones (explicit Kind switch, 400 on unknown, Admin policy)
  - IQueryCityRepository.GetById (AsNoTracking)
  - CoffeePeek.ShopsService.Tests in CoffeePeek.Backend.ci.slnf (D-05)
  - docs/api/public-addresses.md with city/zone contract, allocation rules, catalog filter values and extended live gate
affects: [external-frontend, public-addresses-runbook, live-postgresql-gate, ci]

tech-stack:
  added: []
  patterns:
    - "Public address controllers inject IPublicAddressService and a read-only query directly (no new Wolverine handler, no codegen)"
    - "Visibility re-check on the data read after resolve (zone Status == Published)"
    - "Reflection test ties the admin route regex alternatives to Enum.GetValues<PublicAddressKind>()"

key-files:
  created:
    - CoffeePeek.ShopsService/Controllers/PublicCityAddressesController.cs
    - CoffeePeek.ShopsService/Controllers/PublicCoffeeZoneAddressesController.cs
    - CoffeePeek.ShopsService.Tests/PublicCatalogAddressControllerTests.cs
  modified:
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IQueryCityRepository.cs
    - CoffeePeek.Shops.Persistance/Repositories/QueryCityRepository.cs
    - CoffeePeek.ShopsService/Controllers/AdminPublicAddressesController.cs
    - CoffeePeek.ShopsService.Tests/PublicAddressApiContractTests.cs
    - CoffeePeek.Backend.ci.slnf
    - docs/api/public-addresses.md

key-decisions:
  - "City/zone public address endpoints live under the already-routed api/Catalogs prefix; no gateway change"
  - "Public city/zone controllers inject IPublicAddressService plus IQueryCityRepository/ICoffeeZoneQueries directly instead of IMessageBus, avoiding new handlers and Wolverine codegen"
  - "Zone by-slug re-checks Status == Published on the data read; Draft/Archived return the same 404 as unknown slugs"
  - "City by-slug data is read AsNoTracking from the database, never from city:list:all"
  - "Admin Kind() is an explicit four-arm switch that throws PublicAddressException 400 on anything else"
  - "No slug search params (D-04); docs map readable values to the unchanged GUID filters"

patterns-established:
  - "Admin route/Kind coverage test: regex alternatives must map to exactly the PublicAddressKind enum values"

requirements-completed: [P3-03, P3-04, P3-05, P3-06]

duration: 8min
completed: 2026-09-28
---

# Phase 3 Plan 02: City and Coffee Zone Public Address Endpoints Summary

**City and Published-only coffee zone address lookups (by-slug, batch, legacy GUID) under `api/Catalogs`, admin address management for `cities` and `coffee-zones` through an explicit kind switch, `CoffeePeek.ShopsService.Tests` added to the CI solution filter, and a public-addresses doc that now covers the city/zone contract, catalog filter values and the still-open live PostgreSQL gate.**

## Performance

- **Duration:** about 8 min
- **Started:** 2026-09-28T08:36:30Z
- **Completed:** 2026-09-28T08:44:10Z
- **Tasks:** 2 of 2 (Task 1 TDD: 2 commits; Task 2: 1 commit)
- **Files modified:** 9 (3 created, 6 modified)

## Accomplishments

- `PublicCityAddressesController` (`[Route("api/Catalogs/cities")]`, NoStore): `GetBySlug` resolves with `PublicAddressKind.City`, then reads `IQueryCityRepository.GetById` (new, AsNoTracking). It returns `PublicAddressEnvelope<CityDto>`, or 404 when either the address or the city is missing. `GetBatch` and `GetById` mirror the roaster controller.
- `PublicCoffeeZoneAddressesController` (`[Route("api/Catalogs/coffee-zones")]`, NoStore): `GetBySlug` resolves with `PublicAddressKind.CoffeeZone` and reads `ICoffeeZoneQueries.GetByIdAsync`. It returns 404 when the zone is null or not Published (T-03-01). Data maps to the existing `MapCoffeeZoneDto` with the same fields as `/api/Map`. No new Contract type.
- `AdminPublicAddressesController`: route is now `api/admin/{entity:regex(^(shops|roasters|cities|coffee-zones)$)}/public-addresses`. `Kind()` is a switch on `entity.ToLowerInvariant()` with `_ => throw new PublicAddressException("Unknown address kind.", 400)`. The `[Authorize(Policy = RoleConsts.Admin)]` attribute, NoStore and all action templates are unchanged.
- Contract tests (reflection) cover:
  - New routes and templates.
  - The `CatalogsController` guard (`api/[controller]` + `cities`).
  - NoStore on both new controllers.
  - The exact admin route and Admin policy.
  - `AdminEntityKinds_MapExplicitly` (5 rows, including case-insensitive `Cities`).
  - `AdminRouteEntities_CoverEveryAddressKind`: regex alternatives equal the enum set, and `equipments` gives 400.
- Controller behavior tests use hand-written fakes (no Moq). They cover:
  - Zone: unknown slug, Draft/Archived re-check, Published envelope mapping (all fields, polygon, address), zone missing on read.
  - City: unknown slug, city deleted between resolve and read, envelope mapping.
  - The fakes return data only for the resolved `EntityId`, so a wrong id reads as missing.
- `CoffeePeek.Backend.ci.slnf` now lists `CoffeePeek.ShopsService.Tests` directly after `CoffeePeek.Shops.Infrastructure.Tests` (D-05). The CI workflow is unchanged, because it already restores, builds and tests the slnf.
- `docs/api/public-addresses.md` was retitled to `# Public addresses` and extended:
  - Migration `20260928082712_AddCityAndZonePublicAddresses` added to the enablement gate, with the city/zone live checks marked pending.
  - Prefix table for the four kinds, a city JSON example (`/cities/minsk`) and the visibility rules.
  - New `## Cities and coffee zones` section.
  - Admin entity sentence and Moderator-vs-Admin note.
  - City/zone backfill review guidance (duplicate names, RU/BY variants).
  - New `## Catalog filter values` section.
  - Cache note and `Down` rollback warning.

## Task Commits

1. **Task 1: Public city/zone endpoints, admin kind extension, contract and controller tests**
   - RED `ca38eb22`: `test(03-02): add failing city/zone address endpoint and admin kind tests`
   - GREEN `ee185d94`: `feat(03-02): expose city and published zone public addresses`
2. **Task 2: CI solution filter, API/rollout docs, full verification**
   - `e370ad3b`: `docs(03-02): add ShopsService.Tests to CI and document city/zone addresses`

No refactor commit was needed. No packages or package references were added. No tracked file was deleted.

## Verification

| Check | Result |
|---|---|
| `CoffeePeek.ShopsService.Tests` (Debug and Release `--no-build`) | 25 passed, 0 failed (baseline 11; +6 admin kind/coverage, +8 controller behavior) |
| `WolverinePreGeneratedHandlersTests.ProductionShopsHost_StartsPastWolverineHandlerCheck` | Passed (no handler drift) |
| `WolverinePreGeneratedHandlersTests.EveryDiscoveredHandler_HasPreGeneratedCode` | Passed |
| `CoffeePeek.Shops.Domain.Tests` | 189 passed, 0 failed |
| `CoffeePeek.Shops.Application.Tests` | 224 passed, 0 failed (existing Moq mocks of `IQueryCityRepository` unaffected) |
| `CoffeePeek.Shops.Infrastructure.Tests` | 64 passed, 0 failed |
| `dotnet restore` + `dotnet build CoffeePeek.Backend.ci.slnf --configuration Release --no-restore` | Build succeeded, 0 errors (470 pre-existing warnings, for example CS0618 in Account, NU1903 Microsoft.OpenApi) |
| `has-pending-model-changes` | "No changes have been made to the model since the last migration." |
| `git diff --check` | clean |
| `grep -c CoffeePeek.ShopsService.Tests CoffeePeek.Backend.ci.slnf` | 1 (JSON valid) |
| `grep -rnE 'PublicAddresses(__\|:\|")' CoffeePeek.ShopsService/appsettings*.json deploy/ CoffePeek.AppHost/` | no output (flag default-off, unconfigured) |
| `(ShopTag\|BrewMethod\|CoffeeBean\|Equipment)PublicAddress` files | 0 (D-03) |
| `git diff --quiet d812e0c0 -- CoffeePeek.Contract CoffeePeek.Gateway CoffeePeek.ShopsService/Internal/Generated CoffeePeek.Shops.Application/Features CoffeeShopsController.cs MapController.cs CatalogsController.cs PublicSlug.cs ShopTagAggregate .github/workflows/ci-cd.yml` | exit 0 (D-03, D-04, P3-06) |
| `git diff --quiet d812e0c0 -- CoffeePeek.ShopsService.Tests/CoffeePeek.ShopsService.Tests.csproj` | exit 0 (no package reference added) |
| Task 1 greps | `Route("api/Catalogs/cities")` 1, `Route("api/Catalogs/coffee-zones")` 1, `CoffeeZoneStatus.Published` 1, `shops\|roasters\|cities\|coffee-zones` 1, `RoleConsts.Admin` 1, ternary `? PublicAddressKind` 0 |
| Doc strings | All 14 required strings present |

Endpoint behavior is proven only by reflection and direct controller-action tests. No live HTTP run was made.

**Open live PostgreSQL gate items** (Docker/PostgreSQL unavailable; none verified, all listed as pending in `docs/api/public-addresses.md`):
- Unique `Slug` and filtered canonical owner indexes on `CityPublicAddresses` and `CoffeeZonePublicAddresses` hold under concurrent allocation (two concurrent "Минск" creations serialize on the advisory lock).
- `FK_CityPublicAddresses_Cities_OwnerId` and `FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId` are RESTRICT (deleting a city without retirement fails).
- Deleting a city that still has coffee zones returns 409, and its address retirement rolls back.
- `CK_PublicAddressAudit_Kind` accepts 3 and 4.
- `CK_*_Lifecycle` rejects a canonical row without an owner.
- The phase 1/2 shop/roaster live items (uniqueness, FKs, concurrent allocation/rename, savepoint rollback) remain open.

`PublicAddresses__Enabled` still defaults to false and is configured nowhere.

## Decisions Made

- The key decisions in the frontmatter apply as planned.
- XML doc comments were added on the new public controller actions (CLAUDE.md convention for public controller actions).

## Deviations from Plan

None in behavior. Small additions, all within the planned files:
- The `FakeAddresses` fake records `LastKind`. Two not-found tests assert that the controllers resolve with `PublicAddressKind.CoffeeZone` and `PublicAddressKind.City` respectively.
- The fakes return data only for the matching `EntityId`. This proves the controllers read the resolved owner id rather than any id.
- The zone Published test also asserts the first polygon point mapping, in addition to the polygon length.
- In the doc, the existing single-line prefix sentence was replaced by a prefix table (entity / prefix / data / canonical path). The generic Method/Path table was kept. The Enum/other params list also names `isNew`, `isVisited` and `q`, which exist on `GET api/CoffeeShops`.

## TDD Gate Compliance

- RED `ca38eb22` failed at compile time (CS0246: `PublicCityAddressesController` and `PublicCoffeeZoneAddressesController` not found). The new admin kind assertions could not run until the controllers existed.
- GREEN `ee185d94` passes all 25 tests.

## Known Stubs

None.

## Issues Encountered

None.

## Next Phase Readiness

- The external frontend can resolve `/cities/{slug}` and `/coffee-zones/{slug}` pages and map readable filter values to GUIDs using the documented endpoints.
- Enablement still requires the live PostgreSQL gate (phase 1–3 items) plus dry-run backfill review for cities (duplicate names, RU/BY variants) and Published zones.

## Self-Check: PASSED

- FOUND: CoffeePeek.ShopsService/Controllers/PublicCityAddressesController.cs
- FOUND: CoffeePeek.ShopsService/Controllers/PublicCoffeeZoneAddressesController.cs
- FOUND: CoffeePeek.ShopsService.Tests/PublicCatalogAddressControllerTests.cs
- FOUND: docs/api/public-addresses.md
- FOUND: CoffeePeek.Backend.ci.slnf
- FOUND commits: ca38eb22, ee185d94, e370ad3b
