---
phase: quick-260831-krk
plan: 01
subsystem: api
tags: [wolverine, cqrs, ef-core, mapster, catalogs, rbac, shops-service]

# Dependency graph
requires: []
provides:
  - Full Create/Update/Delete for City, CoffeeBean, Roaster, BrewMethod, Equipment (Shops-service catalog reference types)
  - Five new write-repository interfaces + implementations (ICityRepository, ICoffeeBeanRepository, IRoasterRepository, IBrewMethodRepository, IEquipmentRepository)
  - BrewMethodCategoryEnum Contract type + BrewMethodDto.Category field
  - Five new Admin*Controller classes at /api/admin/{cities|beans|roasters|brew-methods|equipments}
  - AdminShopTagsController policy widened from Admin-only to Moderator (Admin+Moderator)
affects: [shops-service-admin-api, catalog-management]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Admin catalog CRUD: duplicate-check via Query/Write repo GetByName(Async), 409 on duplicate, 404 on missing, DomainException -> 400 via GlobalExceptionHandler, cache invalidation via internal static Invalidate*CachesAsync helper shared across Create/Update/Delete handlers"
    - "Command Id fields use [property: JsonIgnore] and are always route-sourced in the controller, never body-bound"

key-files:
  created:
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/ICityRepository.cs
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/ICoffeeBeanRepository.cs
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IRoasterRepository.cs
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IBrewMethodRepository.cs
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IEquipmentRepository.cs
    - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Cities/AdminCityHandlers.cs
    - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Beans/AdminCoffeeBeanHandlers.cs
    - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Roasters/AdminRoasterHandlers.cs
    - CoffeePeek.Shops.Application/Features/Admin/Catalogs/BrewMethods/AdminBrewMethodHandlers.cs
    - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Equipments/AdminEquipmentHandlers.cs
    - CoffeePeek.Contract/Enums/BrewMethodCategoryEnum.cs
    - CoffeePeek.ShopsService/Controllers/AdminCitiesController.cs
    - CoffeePeek.ShopsService/Controllers/AdminBeansController.cs
    - CoffeePeek.ShopsService/Controllers/AdminRoastersController.cs
    - CoffeePeek.ShopsService/Controllers/AdminBrewMethodsController.cs
    - CoffeePeek.ShopsService/Controllers/AdminEquipmentsController.cs
  modified:
    - CoffeePeek.Shops.Domain/BussinessConstants.cs
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/City.cs
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/CoffeeBean.cs
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Roaster.cs
    - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Equipment.cs
    - CoffeePeek.Shops.Domain/Aggregates/BrewMethods/BrewMethod.cs
    - CoffeePeek.Shops.Persistance/Repositories/QueryCityRepository.cs
    - CoffeePeek.Shops.Persistance/Repositories/QueryCoffeeBeanRepository.cs
    - CoffeePeek.Shops.Persistance/Repositories/QueryRoasterRepository.cs
    - CoffeePeek.Shops.Persistance/Repositories/QueryEquipmentRepository.cs
    - CoffeePeek.Shops.Persistance/Repositories/QueryBrewMethodRepository.cs
    - CoffeePeek.Shops.Persistance/DependencyInjection.cs
    - CoffeePeek.Contract/Dtos/Shop/BrewMethodDto.cs
    - CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs
    - CoffeePeek.ShopsService/Controllers/AdminShopTagsController.cs

key-decisions:
  - "City duplicate check reuses the existing IQueryCityRepository.GetByName (no new GetByNameAsync added to ICityRepository); CoffeeBean/Roaster/BrewMethod add GetByNameAsync directly on their new write repositories since no pre-existing Query-side lookup existed"
  - "Equipment duplicate check is brand+model equality (mirrors CoffeeShop.AddEquipment's existing in-memory check), not name-based like the other four entities"
  - "BrewMethodCategoryEnum added to CoffeePeek.Contract, mirroring the Domain BrewMethodCategory enum, following the exact EquipmentCategoryEnum/EquipmentCategory precedent -- keeps Domain types off the public command/DTO surface"
  - "AdminShopTagsController policy widened from RoleConsts.Admin to RoleConsts.Moderator so all six catalog types (five new + shop-tags) share identical authorization; Moderator policy already grants Admin via RequireRole(Moderator, Admin)"

patterns-established:
  - "Admin catalog write-repository interfaces live alongside the existing IQuery*Repository interfaces in the same folder/namespace (CoffeeShopAggregate/Repositories), even for entities like BrewMethod whose class is declared in a different namespace (Aggregates.BrewMethods)"

requirements-completed: ["QUICK-260831-krk"]

# Metrics
duration: ~30min
completed: 2026-08-31
---

# Quick Task 260831-krk: Full CRUD for Shops-service catalogs Summary

**Create/Update/Delete for City, CoffeeBean, Roaster, BrewMethod, and Equipment catalogs added via new write-repositories, CQRS/Wolverine handlers, and five Admin*Controller classes, all gated by the existing Moderator+Admin role policy; AdminShopTagsController's policy was widened to match.**

## Performance

- **Duration:** ~30 min
- **Completed:** 2026-08-31
- **Tasks:** 5/5
- **Files modified:** 27 (16 created, 11 modified) across Domain, Persistence, Application, and API layers

## Accomplishments
- All six Shops-service catalog reference types (cities, beans, equipments, roasters, brew-methods, shop-tags) now have full Create/Update/Delete, restricted to Admin/Moderator roles
- Domain entities (City, CoffeeBean, Roaster, BrewMethod) gained public constructors + `Update()` mutators with `DomainException` validation; `Equipment` gained an `Update()` mutator matching its constructor's `ArgumentException` style
- Five new write-repository interfaces + EF Core implementations, registered in DI
- `BrewMethodCategoryEnum` added to the Contract layer; `BrewMethodDto.Category` now round-trips (also fixes a pre-existing gap where `GetAllBrewMethodsHandler` silently dropped `Category`)
- Five new `Admin*Controller` classes at `/api/admin/{cities|beans|roasters|brew-methods|equipments}`; `AdminShopTagsController` policy widened from Admin-only to Moderator (which already includes Admin)
- No EF Core migration required; no `.csproj`/`Directory.Packages.props` changes — verified via `git diff --stat`

## Task Commits

Each task was committed atomically:

1. **Task 1: Domain — constructible/mutable catalog entities + write-repository contracts** - `df4c8e93` (feat)
2. **Task 2: Persistence — write repositories + DI registration** - `666c6ee1` (feat)
3. **Task 3: Application — Create/Update/Delete for Cities, Beans, Roasters** - `93c83322` (feat)
4. **Task 4: Application — Create/Update/Delete for BrewMethods and Equipment** - `483133d4` (feat)
5. **Task 5: API — five new Admin controllers, widen shop-tags policy, full solution verification** - `93312af8` (feat)

_Note: tasks were TDD-tagged (1, 3, 4) but committed as single feat commits per task per the orchestrator's atomic-commit-per-task constraint (docs-only files excluded from these commits)._

## Files Created/Modified

**Domain:**
- `CoffeePeek.Shops.Domain/BussinessConstants.cs` - New `#region Catalogs` with MaxCityNameLength/MaxCoffeeBeanNameLength/MaxRoasterNameLength/MaxBrewMethodNameLength
- `CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/City.cs` - Public constructor + `Update(string name)`, `DomainException` validation
- `CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/CoffeeBean.cs` - Public constructor + `Update`, private EF ctor
- `CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Roaster.cs` - Public constructor + `Update`, private EF ctor
- `CoffeePeek.Shops.Domain/Aggregates/BrewMethods/BrewMethod.cs` - Public constructor + `Update(name, category)`, private EF ctor
- `CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Equipment.cs` - `Update(brand, modelName, category)` mutator
- `.../Repositories/ICityRepository.cs`, `ICoffeeBeanRepository.cs`, `IRoasterRepository.cs`, `IBrewMethodRepository.cs`, `IEquipmentRepository.cs` - New write-repository contracts

**Persistence:**
- `QueryCityRepository.cs`, `QueryCoffeeBeanRepository.cs`, `QueryRoasterRepository.cs`, `QueryEquipmentRepository.cs`, `QueryBrewMethodRepository.cs` - Each gained a sibling write-repository class (`CityRepository`, `CoffeeBeanRepository`, etc.)
- `DependencyInjection.cs` - Registered five new write repositories

**Application:**
- `Features/Admin/Catalogs/Cities/AdminCityHandlers.cs` - Create/Update/Delete for City
- `Features/Admin/Catalogs/Beans/AdminCoffeeBeanHandlers.cs` - Create/Update/Delete for CoffeeBean
- `Features/Admin/Catalogs/Roasters/AdminRoasterHandlers.cs` - Create/Update/Delete for Roaster
- `Features/Admin/Catalogs/BrewMethods/AdminBrewMethodHandlers.cs` - Create/Update/Delete for BrewMethod
- `Features/Admin/Catalogs/Equipments/AdminEquipmentHandlers.cs` - Create/Update/Delete for Equipment (with category resolution)
- `Mapper/MapsterConfiguration.cs` - New `BrewMethod -> BrewMethodDto` config mapping `Category`

**Contract:**
- `Enums/BrewMethodCategoryEnum.cs` - New Contract-side mirror of Domain `BrewMethodCategory`
- `Dtos/Shop/BrewMethodDto.cs` - Additive `Category` property

**API:**
- `Controllers/AdminCitiesController.cs`, `AdminBeansController.cs`, `AdminRoastersController.cs`, `AdminBrewMethodsController.cs`, `AdminEquipmentsController.cs` - New admin controllers
- `Controllers/AdminShopTagsController.cs` - Policy widened `RoleConsts.Admin` -> `RoleConsts.Moderator`

**Tests:**
- `CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/CityTests.cs`, `CoffeeBeanTests.cs`, `RoasterTests.cs` (new)
- `CoffeePeek.Shops.Domain.Tests/Aggregates/BrewMethods/BrewMethodTests.cs` (new)
- `CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/EquipmentTest.cs` (appended `Update` tests)
- `CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/{Cities,Beans,Roasters,BrewMethods,Equipments}/*HandlersTests.cs` (new)

## Decisions Made
- City's duplicate check reuses the pre-existing `IQueryCityRepository.GetByName` instead of adding a redundant method to the new `ICityRepository`; CoffeeBean/Roaster/BrewMethod add `GetByNameAsync` directly to their write repositories since no equivalent Query-side lookup existed for them.
- Equipment duplicate detection uses brand+model equality (mirroring `CoffeeShop.AddEquipment`'s existing in-memory dedup logic) rather than a name-based check, since Equipment names are derived (`$"{Brand} {ModelName}"`).
- `BrewMethodCategoryEnum` was added to `CoffeePeek.Contract` as a straight mirror of the Domain `BrewMethodCategory` enum, following the existing `EquipmentCategoryEnum`/`EquipmentCategory` precedent — keeps Domain enums off the Application command surface.
- `AdminShopTagsController`'s authorization policy was widened from `RoleConsts.Admin` to `RoleConsts.Moderator` so all six catalog types share identical authorization (Moderator policy already grants Admin via `RequireRole(Moderator, Admin)`).

## Deviations from Plan

None - plan executed exactly as written. All five tasks matched their `<action>` specs; no Rule 1-4 deviations were needed.

## Issues Encountered

The plan's PLAN.md file existed only in the main repository working tree (untracked, not yet committed) and was not visible from the git worktree used for execution, since `.planning/` is untracked in this repo and worktrees only materialize tracked files. The plan was read directly from the main repo's absolute path. This SUMMARY.md is written inside the worktree at the relative `.planning/quick/.../260831-krk-SUMMARY.md` path (per Write-tool worktree isolation), not committed to git per the orchestrator's instruction to leave docs artifacts for the orchestrator's separate docs commit. All code changes were made and committed inside the worktree as normal.

## User Setup Required

None - no external service configuration required. No EF Core migration needed (verified via `git diff --stat` showing zero changes to `*.csproj`, `Directory.Packages.props`, or any `Migrations/` file across all five task commits).

## Next Phase Readiness

- All six Shops-service catalog types now have full CRUD, ready for admin/moderator use
- Full solution build succeeds (0 errors) and full test suite passes: 803 tests across 7 test projects (116 Shops.Domain.Tests, 89 Shops.Application.Tests, 219 Account.Domain.Tests, 175 Account.Application.Tests, 155 Moderation.Domain.Tests, 46 Account.Infrastructure.Tests, 3 Gateway.Tests)
- No blockers for follow-on work (e.g., frontend admin UI for these endpoints, if planned separately)

---
*Phase: quick-260831-krk*
*Completed: 2026-08-31*

## Self-Check: PASSED

- Verified files exist: ICityRepository.cs, AdminEquipmentHandlers.cs, AdminBrewMethodsController.cs, BrewMethodCategoryEnum.cs
- Verified commits exist in `git log`: df4c8e93, 666c6ee1, 93c83322, 483133d4, 93312af8
- Full solution build: 0 errors
- Full solution test run: 803/803 tests passed across 7 test projects (Shops.Domain.Tests, Shops.Application.Tests, Account.Domain.Tests, Account.Application.Tests, Moderation.Domain.Tests, Account.Infrastructure.Tests, Gateway.Tests)
- `git diff --stat` confirms no `.csproj`/`Directory.Packages.props`/`Migrations/` changes across all task commits
