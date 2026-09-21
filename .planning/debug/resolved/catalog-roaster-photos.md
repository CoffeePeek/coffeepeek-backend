---
status: resolved
trigger: "GET /api/Catalogs/roasters does not return roaster photos"
created: 2026-09-21
updated: 2026-09-21T12:18:00+03:00
---

# Debug Session: Catalog Roaster Photos

## Symptoms
- GET `/api/Catalogs/roasters` returns roasters without photo data.
- `RoasterDto` exposes `PhotoUrl` and `CoverPhoto`.
- Mapper behavior is already covered when `Roaster.Photos` is populated.

## Current Focus
reasoning_checkpoint:
  hypothesis: "`QueryRoasterRepository.GetAll()` causes missing catalog photo data because it executes a fresh no-tracking roaster query without including the non-auto-loaded `Photos` navigation."
  confirming_evidence:
    - "The controller/handler catalog path calls `GetAll()` and maps its entities directly to `RoasterDto[]`."
    - "A focused persistence test persisted one roaster photo, cleared tracking, then observed `GetAll()` return the roaster with an empty `Photos` collection."
    - "The same repository explicitly includes `Photos` for the by-id path, and mapper tests populate photo fields when the navigation is loaded."
  falsification_test: "If adding `Include(r => r.Photos)` leaves the focused persistence test failing or returns the photo while the unmodified query also does, this hypothesis is wrong."
  fix_rationale: "Eager-loading the navigation at the repository boundary supplies the photo data already required by the existing catalog mapper without changing public contracts or unrelated query paths."
  blind_spots: "The focused test uses EF InMemory rather than PostgreSQL; provider-specific SQL will be covered by compiling/running the full infrastructure test project, but no live PostgreSQL endpoint test is available."
next_action: Archive the resolved session and record the confirmed pattern in the debug knowledge base.

## Evidence
- timestamp: 2026-09-21T11:52:00+03:00
  observation: `QueryRoasterRepository.GetAll()` reportedly calls `AsNoTracking().ToArrayAsync()` without including `Photos`.
- timestamp: 2026-09-21T11:52:00+03:00
  observation: `GetByIdWithShopsAndPhotosAsync` reportedly includes `Photos`, showing eager loading is already used for this navigation.
- timestamp: 2026-09-21T11:52:00+03:00
  observation: Existing mapper tests reportedly pass when `Photos` is populated.
- timestamp: 2026-09-21T11:56:00+03:00
  observation: `CatalogsController.GetRoasters` dispatches `GetAllRoastersCommand`; `GetAllRoastersHandler` calls `IQueryRoasterRepository.GetAll()` and directly maps the returned entities to `RoasterDto[]`.
- timestamp: 2026-09-21T11:56:00+03:00
  observation: Source search confirms `QueryRoasterRepository.GetAll()` uses only `AsNoTracking().ToArrayAsync()`, while `GetByIdWithShopsAndPhotosAsync` explicitly includes the photo navigation.
- timestamp: 2026-09-21T11:56:00+03:00
  observation: `RoasterMappingTests` already prove `PhotoUrl` is populated when `Roaster.Photos` contains a photo, narrowing the divergence to persistence materialization rather than DTO mapping.
- timestamp: 2026-09-21T12:00:00+03:00
  observation: The EF relationship maps `Roaster.Photos` to the private `_photos` field, but the navigation is not configured for automatic eager loading; therefore a fresh no-tracking query must explicitly include it.
- timestamp: 2026-09-21T12:00:00+03:00
  observation: Existing infrastructure persistence tests use provider-backed `ShopsDbContext` instances but have no execution fixture; a test-scoped EF InMemory provider can reproduce navigation materialization without requiring external PostgreSQL.
- timestamp: 2026-09-21T12:04:00+03:00
  observation: The focused `GetAll_LoadsRoasterPhotos` persistence test fails before the fix with `Expected result[0].Photos to contain a single item, but the collection is empty`, directly reproducing the catalog data loss after clearing EF tracking.
- timestamp: 2026-09-21T12:07:00+03:00
  observation: After adding `Include(r => r.Photos)` to `GetAll()`, the same focused regression test passes (1/1), confirming the eager-load omission was causal.
- timestamp: 2026-09-21T12:10:00+03:00
  observation: The complete `CoffeePeek.Shops.Infrastructure.Tests` project passes 10/10 tests, including the new persistence regression.
- timestamp: 2026-09-21T12:10:00+03:00
  observation: Focused `RoasterMappingTests` pass 2/2, confirming the loaded navigation still maps to the catalog photo fields.
- timestamp: 2026-09-21T12:14:00+03:00
  observation: Commit `48c699c4` contains only the repository fix, focused regression test, and its two dependency declarations; all unrelated worktree changes, including the generated Wolverine file, remain uncommitted and untouched.
- timestamp: 2026-09-21T12:18:00+03:00
  observation: Human verification accepted the independently rerun focused repository test (1/1) and mapper tests (2/2) as sufficient; a live PostgreSQL endpoint check is explicitly not required for this task.

## Resolution
- root_cause: `QueryRoasterRepository.GetAll()` queried roasters with `AsNoTracking()` but omitted `Include(r => r.Photos)`. Because the navigation is neither auto-included nor lazy-loaded, the catalog handler mapped an empty photo collection and emitted no photo fields.
- fix: Added eager loading of `Roaster.Photos` to `QueryRoasterRepository.GetAll()` and a focused persistence regression test that saves a photo, clears tracking, and verifies the repository materializes it.
- verification: Focused regression RED before fix and GREEN after fix (1/1); full Shops infrastructure tests pass (10/10); focused roaster mapper tests pass (2/2), including an independent rerun accepted as final verification. A live PostgreSQL endpoint check was explicitly not required. Existing dependency vulnerability and nullable warnings remain unrelated.
- files_changed:
  - Directory.Packages.props
  - CoffeePeek.Shops.Infrastructure.Tests/CoffeePeek.Shops.Infrastructure.Tests.csproj
  - CoffeePeek.Shops.Infrastructure.Tests/Persistence/QueryRoasterRepositoryTests.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryRoasterRepository.cs
  - .planning/debug/catalog-roaster-photos.md
