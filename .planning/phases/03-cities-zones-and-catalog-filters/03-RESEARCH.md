# Phase 3: Cities, zones and catalog filters - Research

**Researched:** 2026-09-28
**Domain:** Extending the phase 1–2 public-address registry (EF Core 10 + PostgreSQL, ASP.NET Core controllers) to City and CoffeeZone; catalog filter values
**Confidence:** HIGH for codebase facts (file:line verified, spikes run); MEDIUM for product-shape recommendations (need user confirmation, see Open Questions)

## User Constraints

> Written before `03-CONTEXT.md`; user decisions D-01..D-05 there are authoritative. The constraints below come from the orchestrator's phase description, `.planning/ROADMAP.md` (Phase 3) and the approved spec `.planning/specs/PUBLIC-SLUGS.md`. Treat them as locked.

### Locked Decisions
- Reuse the phase 1–2 slug rules (`CoffeePeek.Shared.Domain/PublicAddresses/PublicSlug.cs`), typed registries, transactional coordinator (`CoffeePeek.Shops.Persistance/PublicAddresses/*`, `ShopsDbContext.SaveChangesAsync` hook), visibility-aware lookup, rename/history and bounded backfill.
- Existing GUID routes and DTOs stay unchanged. Additive DTO usage only; `CoffeePeek.Contract` types must not be modified without versioning.
- Feature stays default-off (`PublicAddresses__Enabled`) behind the same live PostgreSQL gate.
- Spec: "Slug namespaces are per entity type" (PUBLIC-SLUGS.md:11). "Use typed registries with real foreign keys" (PUBLIC-SLUGS.md:15). "Visibility checks must match existing public entity rules" (PUBLIC-SLUGS.md:23). "Generation rules must not retroactively rewrite published addresses" (PUBLIC-SLUGS.md:9).
- Migrations only via the Makefile (`make mig-shops n=...`).
- Do not rename `CoffePeek.*`, `Shops.Persistance`, `CoffeeShop.Moderation.*`.

### Claude's Discretion
- Which catalog values get registries (spec step 5 says "approved catalogs/cities/zones", PUBLIC-SLUGS.md:33). This research recommends a minimal set; the user must confirm (see Open Questions).
- Zone namespace shape, path prefixes, allocation timing, endpoint placement.

### Deferred Ideas (OUT OF SCOPE)
- Account public profiles (phase 4).
- External frontend routing, 301 redirects, canonical tags, sitemaps (phase 6). There is no frontend in this repo.
- Production backfill or enablement (phase 5 / deployment authorization).

## Project Constraints (from CLAUDE.md)

- .NET 10 / C# 14. Do not change the stack. Central package management (`Directory.Packages.props`). No new packages are needed for this phase.
- Layered DDD: Domain → Application → Infrastructure/Persistence. Controllers stay thin.
- Wolverine handlers are static `Handle` methods. Production uses `TypeLoadMode.Static`, so **any new or changed handler signature requires `dotnet run -- codegen write`** in `CoffeePeek.ShopsService` and committing `Internal/Generated/WolverineHandlers/`. This is enforced by `CoffeePeek.ShopsService.Tests/WolverinePreGeneratedHandlersTests.cs`. The recommended design below changes **no** handler signatures and adds no handlers.
- Conventions: file-scoped namespaces, primary constructors, `record` DTOs, `_camelCase` fields, `ct` in handlers, one class per file (the phase 2 files group small types; follow the existing file you extend).
- Testing: xUnit v3, FluentAssertions, Moq. Use `TestContext.Current.CancellationToken`.
- Core value: every fixed behavior needs a regression test.
- GSD workflow: edits go through `/gsd-execute-phase`.
- Note: CLAUDE.md says Wolverine 5.30, but `Directory.Packages.props:28-31` pins **WolverineFx 6.35.0** [VERIFIED: codebase]. This does not matter for this phase because no Wolverine surface changes.

## Summary

Phase 2 built a coordinator that is **already generic over the row type** (`ResolveAsync<T>`, `ChangeAsync<T>`, `BackfillAsync<T>`, `FindCandidateAsync<T>`, `InitializeAsync<T>`, `RetireAsync<T>` all take `T : PublicAddress`). What is kind-specific is dispatched with **binary ternaries** (`kind == CoffeeShop ? … : …`), in about 12 places across `PublicAddressService.cs`, `PublicAddressWriter.cs` and `AdminPublicAddressesController.cs`. Each place defaults to the roaster branch. The smallest correct extension adds two typed registries (`CityPublicAddresses`, `CoffeeZonePublicAddresses`), two enum values, and replaces every ternary with an explicit 4-arm `switch` that throws on unknown kinds. No new abstraction is needed. One schema trap must be handled: `CK_PublicAddressAudit_Kind` is hard-coded to `"Kind" IN (1, 2)` (`PublicAddressAuditConfiguration.cs:19`). If it is not widened in the same migration, the first city or zone audit insert fails.

The minimal set, based on evidence: **City and CoffeeZone get registries.** **ShopTag does not.** It already has an immutable (SHOP-FILTERS-TAGS.md:30), uniquely indexed (`ShopTagConfiguration.cs:26`) slug that is part of a cross-service contract (`ImportCandidatePublishedEvent.TagSlugs`, `CoffeePeek.Contract/Events/Moderation/ImportCandidatePublishedEvent.cs:18`). It is never physically deleted (soft-deactivate only) and is already public through `GET /api/Catalogs/shop-tags`. Its underscore format (`laptop_friendly`) fails `PublicSlug.IsValid`. That is fine for a query-string value, and changing it would break Moderation. Roasters were done in phase 2. BrewMethod, CoffeeBean and Equipment have no slug and no public page, and appear only as GUID arrays in `GET /api/CoffeeShops`. Defer them.

There are **no public page endpoints for cities or zones today**. Cities appear only in `GET /api/Catalogs/cities`, which lists every city with no filter. Zones appear only inside `GET /api/Map` (Published only, zoom 11–13). All public search filters are GUIDs. The lean approach follows phase 2 exactly. Add `by-slug`, batch `public-addresses?ids=` and `{id}/public-address` endpoints for cities and zones **under the existing `api/Catalogs` prefix**, which the gateway already routes (`CoffeePeek.Gateway/appsettings.json:188-191`), so no gateway change is needed. Extend the admin route regex. Allocate city addresses on creation and zone addresses on first publication. Leave the search/map query contracts untouched: the external frontend resolves slug → GUID with the new endpoints.

**Primary recommendation:** One plan with three tasks mirroring 02-01: (1) registries + migration + SaveChanges hook, (2) service switch dispatch + public/admin endpoints + tests, (3) docs/runbook + verification. Global per-type zone namespace. Flat canonical paths `/cities/{slug}` and `/coffee-zones/{slug}`.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Slug generation/validation (RU/BY translit, reserved words) | Shared Domain (`PublicSlug`) | — | Already shared; unchanged |
| Address row lifecycle (canonical/alias/tombstone) | Shops Domain (`PublicAddress` subclasses) | — | Two new sealed subclasses, same base |
| Uniqueness, at-most-one canonical, restrictive FK | Database (PostgreSQL) | Persistence config | DB is the uniqueness authority (spec) |
| Allocation on create/publish, retirement on delete | Persistence (`ShopsDbContext.SaveChangesAsync` hook + `PublicAddressWriter`) | — | Intercepts every supported write, with no handler changes |
| Visibility (city exists; zone Published) | Persistence (`PublicAddressService`) | — | Read live DB, never caches |
| Public lookup/batch/legacy-GUID endpoints | API (ShopsService controllers under `api/Catalogs`) | Gateway (existing route) | No gateway change |
| Admin initialize/rename/history/backfill | API (`AdminPublicAddressesController`) | Gateway (existing `/api/admin/cities/**`, `/api/admin/coffee-zones/**` routes) | Service enforces Admin policy |
| Page redirects, canonical tags, filter-URL composition | External frontend | — | Out of scope (phase 6) |

## Standard Stack

No new libraries. Everything already exists in the repo [VERIFIED: codebase, `Directory.Packages.props`].

| Library | Version | Purpose | Note |
|---------|---------|---------|------|
| Microsoft.EntityFrameworkCore | 10.0.5 | Model, change tracking hook | `Directory.Packages.props:8` |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.1 | Registry tables, advisory lock, unique-violation detection | `Directory.Packages.props:13` |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.5 | Behavior tests (not constraint proof) | `Directory.Packages.props:66` |
| dotnet-ef tool | 10.0.5 | Migration generation | `dotnet ef --version` → 10.0.5 [VERIFIED: local] |
| xUnit v3 / FluentAssertions / Moq | per props | Tests | existing |

**Installation:** none.

## Package Legitimacy Audit

This phase installs no external packages, so slopcheck was not run. **Packages removed:** none. **Packages flagged:** none.

<phase_requirements>
## Phase Requirements

No REQUIREMENTS.md IDs exist for this phase. The phase goal decomposes into these behaviors (P3-xx are research labels for the planner, not official IDs):

| Label | Behavior | Research Support |
|-------|----------|------------------|
| P3-01 | City has stable readable address; allocated on create, retired on delete, never renamed by display-name edit | Lifecycle hooks §, Pattern 2 |
| P3-02 | Published CoffeeZone has stable address; hidden when Draft/Archived; not retired by archive | Visibility rules §, Pattern 2 |
| P3-03 | Visibility-aware by-slug / batch / legacy-GUID lookup for cities and zones; alias → current canonical | Pattern 3 |
| P3-04 | Admin initialize/rename(A→B→A)/history/backfill for cities and zones | Pattern 4 |
| P3-05 | Catalog filter values: documented stable values for URLs (tags existing slug; city/zone/roaster via address endpoints); GUID filter contracts unchanged | Filter URL analysis § |
| P3-06 | Existing GUID routes/DTOs unchanged; default-off; live PG gate extended | Pitfalls, Validation |
</phase_requirements>

## Evidence: entities in scope and public visibility (Q1)

| Entity | Public exposure today (evidence) | Existing slug? | Delete path | Recommendation |
|---|---|---|---|---|
| **City** (`Shops.Domain/.../Entities/City.cs:7-35`, only `Name`, no status) | `GET /api/Catalogs/cities` returns **all** cities, no filter (`CatalogsController.cs:19-29` → `GetCitiesRequestHandler` → `QueryCityRepository.GetAll` `QueryCityRepository.cs:11-14`). Search filter `cityId: Guid?` (`CoffeeShopsController.cs:34`) | No | Physical `Remove` in `DeleteCityHandler` (`AdminCityHandlers.cs:70-89`) | **Registry.** Visible if it exists (matches current catalog rule). "Only cities with shops" would be a new product rule (Open Question 2) |
| **CoffeeZone** (`CoffeeZone.cs`, `Status` Draft=0/Published=1/Archived=2, `CoffeeZoneStatus.cs:3-8`) | Only in `GET /api/Map` response, **Published only** (`CoffeeShopQueries.cs:261-262`), `MapCoffeeZoneDto` with GUID `Id` (`Contract/Dtos/CoffeeShop/MapCoffeeZoneDto.cs:3-11`). No public zone detail or zone filter endpoint | No | **None.** The admin DELETE archives (`AdminCoffeeZonesController.cs:57-60`), and `ICoffeeZoneRepository` has no Remove (`ICoffeeZoneRepository.cs:3-11`) | **Registry.** Visible iff `Status == Published`. Archive/draft hides the address and does not retire it, because Archived is reversible: `Publish()`/`MoveToDraft()` have no guards (`CoffeeZone.cs:79-83`) |
| **ShopTag** (`ShopTag.cs:8`, `Slug`, `NormalizeSlug` lowercases, spaces→`_`, `ShopTag.cs:67-79`) | `GET /api/Catalogs/shop-tags` returns active tags with `slug` (`GetShopTagsHandler.cs`, `AdminShopTagMapper.ToPublicDto`). Search filter is `tags: Guid[]` (`CoffeeShopsController.cs:40`, `CoffeeShopQueries.cs:93-100`) | **Yes.** Unique index (`ShopTagConfiguration.cs:26`), immutable after create (SHOP-FILTERS-TAGS.md:30), seeded with fixed IDs (`20260801164731_AddShopTags.cs:80-84`, `20260815103236_AddCoffeeFocusAndFormatTags.cs:26-28`) | Soft deactivate only (`DeactivateShopTagHandler`) | **No registry.** The existing slug is already stable and unique, and it is a cross-service contract (Moderation `TagSlugs` → `CreateShopFromImportService.cs:60-64`). `laptop_friendly` fails `PublicSlug.IsValid` [VERIFIED: spike], so it cannot join the registry without breaking Moderation. Use it as the filter value as-is |
| **Roaster** | Phase 2 registry done; filter `roasters: Guid[]` | Phase 2 | Phase 2 handles | No work; the frontend maps via `/api/roasters/public-addresses?ids=` |
| **BrewMethod / CoffeeBean / Equipment** (`BrewMethod.cs`, `CoffeeBean.cs`, `Equipment.cs`) | Catalog lists (`/api/Catalogs/brew-methods|beans|equipments`) plus GUID filters (`CoffeeShopsController.cs:37-39`). No pages | No | Physical `Remove` (`AdminBrewMethodHandlers.cs:84`, `AdminCoffeeBeanHandlers.cs:82`, `AdminEquipmentHandlers.cs:95`) | **Defer.** They would need three more registries and delete wiring for values that have no page. Product decision (Open Question 3) |
| EquipmentCategory | int ID, admin-only | — | — | Out |

## Evidence: generality of the phase 2 coordinator (Q2)

Already generic (reuse unchanged): `PublicAddress` base (`Shared.Domain/PublicAddresses/PublicAddress.cs`), `PublicAddressConfiguration.Configure<T>` (`PublicAddressConfiguration.cs:28-49`), `PublicAddressTransaction.RunAsync` (single advisory lock `7402100101` for all kinds, `PublicAddressTransaction.cs:21`), `FindCandidateAsync<T>`, `InitializeAsync<T>`, `RetireAsync<T>`, `Audit` (`PublicAddressWriter.cs:32-80`), and generic `ResolveAsync<T>/GetByIdsAsync<T>/ChangeAsync<T>/BackfillAsync<T>` bodies (`PublicAddressService.cs`). The contract DTOs `PublicAddressMetadata/Envelope/ChangePublicAddressRequest/History/Backfill` (`PublicAddressDtos.cs:3-8`) are kind-agnostic.

The binary kind dispatch that **must** become explicit 4-arm switches is listed below. Every one of these defaults to Roaster, so forgetting one silently mis-routes a City or Zone:

| # | Location | Current | Change |
|---|---|---|---|
| 1 | `IPublicAddressService.cs:6` | `enum { CoffeeShop = 1, Roaster = 2 }` | add `City = 3, CoffeeZone = 4` (values persist in `PublicAddressAudit.Kind`, never renumber) |
| 2 | `PublicAddressAuditConfiguration.cs:19` | `"Kind" IN (1, 2)` | `"Kind" IN (1, 2, 3, 4)` (migration drops and re-adds the check) |
| 3 | `PublicAddressService.cs:18-20` Resolve dispatch | ternary | switch |
| 4 | `:37-39` + `:44-46` batch dispatch + visibility list | ternary | switch; City: `db.Cities` ids; Zone: `Status == Published` |
| 5 | `:59-61` Change dispatch | ternary | switch |
| 6 | `:114-116` + `:122-126` backfill dispatch + owner listing | ternary | switch; City: all, stem `Name`; Zone: **Published only**, stem `Name`, projected with `Select` |
| 7 | `:148-153` `GetStemAsync` | Roaster-else-Shop | switch; zone via `.Select(z => z.Name)` (do not materialize the entity, see Pitfall 3) |
| 8 | `:154-156` `IsVisibleAsync` | ternary | switch; City `AnyAsync(Id)`; Zone `AnyAsync(Id && Status == Published)` |
| 9 | `:157-158` `Metadata` path | `coffee-shops`/`roasters` | add `cities`, `coffee-zones` |
| 10 | `:164-169` `IsAddressConflict` | shop/roaster/audit prefixes | add `IX_CityPublicAddresses_`, `IX_CoffeeZonePublicAddresses_`. If missing, a concurrent rename conflict becomes a 500 |
| 11 | `PublicAddressWriter.cs:57-60` `Create<T>` | `typeof(T)==Shop ? … : Roaster` | explicit type switch + throw. A City would otherwise be created as a `RoasterPublicAddress` and fail the cast |
| 12 | `PublicAddressWriter.cs:12-27` `PrepareOwnerChangesAsync` | shops/roasters only | add city retire/initialize, zone retire/publish-initialize |
| 13 | `ShopsDbContext.cs:33-35` `ownerChanges` gate | `CoffeeShop or Roaster` | add City (Added/Deleted), CoffeeZone (Deleted; newly Published) |
| 14 | `AdminPublicAddressesController.cs:11` route regex + `:39-40` `Kind()` | `shops|roasters`; `shops ? Shop : Roaster` | regex `shops|roasters|cities|coffee-zones`; explicit switch. Extending only the regex would map `cities` → Roaster |

Required new pieces: `CityPublicAddress`, `CoffeeZonePublicAddress` (clones of `CoffeeShopPublicAddress.cs:5-10`); `CityPublicAddressConfiguration` / `CoffeeZonePublicAddressConfiguration` with `HasOne<City>()` / `HasOne<CoffeeZone>()` `.OnDelete(DeleteBehavior.Restrict)` (clone `PublicAddressConfiguration.cs:10-26`); two `DbSet`s + `ApplyConfiguration` in `ShopsDbContext.cs:23-25,83-85`. Shop and roaster behavior is untouched provided arms 1–2 of every switch keep their current logic. Lock that with the existing 6 infra and 3 contract tests.

## Evidence: zone slug namespace (Q3)

The current model reserves one namespace per table: unfiltered unique `Slug` index (`PublicAddressConfiguration.cs:37`). Tombstones null the owner (`CK_{table}_Lifecycle`, `:44`; `PublicAddress.Retire` sets `OwnerId = null`, `PublicAddress.cs:33-38`). Per-city uniqueness (`/minsk/zones/nemiga` with `nemiga` reusable in another city) would require:
- a denormalized non-null `CityId` column on zone address rows (tombstones have no owner to join through),
- `UNIQUE (CityId, Slug)` instead of `UNIQUE (Slug)`, which breaks `PublicAddressConfiguration.Configure<T>`,
- two-key lookups everywhere. `ResolveAsync<T>` uses `SingleOrDefaultAsync(a => a.Slug == normalized)` (`PublicAddressService.cs:25`), which would **throw** on duplicates across cities. `FindCandidateAsync<T>` (`PublicAddressWriter.cs:45-55`) and `ChangeAsync<T>` target lookup (`:77`) would also have to fork.

It would also contradict the approved spec ("Slug namespaces are per entity type", PUBLIC-SLUGS.md:11).

**Recommend a global per-type zone namespace** with a flat canonical path `/coffee-zones/{slug}`. It needs zero registry changes. A zone cannot change city (`CoffeeZone.cs:32-33`), so a frontend can still *display* a city context. Collisions (for example two cities with an "Октябрьская" zone) get the deterministic `-<8hex>` suffix, and admins can pick a manual slug through initialize/rename. Nested URLs are a user decision (Open Question 1). If chosen, it is a larger change than the rest of the phase combined.

## Evidence: lifecycle hooks (Q4)

| Path | File:line | Address action (recommended) |
|---|---|---|
| City create (admin only; no seeds, no import creation. `new City(` appears only here) | `AdminCityHandlers.cs:29-31` → `UnitOfWork.SaveChangesAsync` | Hook: Added City → `InitializeAsync<CityPublicAddress>(stem = Name)` when enabled |
| City rename (display name) | `AdminCityHandlers.cs:60-61` | **None**. Display edit never renames URL (spec) |
| City delete | `AdminCityHandlers.cs:83-84` | Hook: Deleted City → `RetireAsync` all rows (even when disabled, same as shops `PublicAddressWriter.cs:16-19`) |
| City delete blocked by zones | FK `CoffeeZones.CityId → Cities` Restrict (`CoffeeZoneConfiguration.cs:30`; snapshot `:1538-1545`) → `DbUpdateException` → `ConflictException` 409 (`Shared.Persistence/Data/UnitOfWork.cs:16-19`); retirement rolled back by `PublicAddressTransaction` | Live-PG gate item (InMemory cannot prove it, see Pitfall 4) |
| Shops referencing city | **No FK** from `Shops.CityId` to `Cities` (snapshot `:1306-1308`, only an index `:1332`) | Pre-existing: deleting a city orphans shops. Out of scope; do not "fix" here |
| Zone create | `CoffeeZoneMessages.cs:85-87` (always Draft, `CoffeeZone.cs:26`) | None (drafts get no automatic address) |
| Zone update | `CoffeeZoneMessages.cs:108-109` | None |
| Zone status | `CoffeeZoneMessages.cs:127-135`; DELETE = archive (`AdminCoffeeZonesController.cs:57-60`) | Hook: CoffeeZone with `Status == Published` and (Added, or Modified with `Status` IsModified) → `InitializeAsync` (idempotent, returns existing canonical). Draft/Archive: no action |
| Zone physical delete | none exists | Include Deleted-zone retirement in the hook anyway: two lines, and it keeps the Restrict FK invariant safe |
| Seeds / fixed IDs | Cities: none (`InsertData` only in AddShopTags, AddCoffeeFocusAndFormatTags, AddShopMenu). `CitiesConsts.MinskId` (`Contract/Constants/CitiesConsts.cs:5`) is **not** a Cities row id (see `20260815150854_RemapOsmImportCityIds.cs:13-25`) | Never use `CitiesConsts.MinskId` as an address owner in tests or backfill |
| Raw SQL writes | `RemapOsmImportCityIds` updates Shops only | None |

Allocating zones on **first publish** rather than on create avoids permanently reserving draft or candidate names. Every rename leaves a permanent alias. Admins can still pre-choose a slug for a draft through `POST api/admin/coffee-zones/public-addresses/{id}/initialize`, and publish then keeps it because `InitializeAsync` returns the existing canonical row (`PublicAddressWriter.cs:35-37`). [VERIFIED: InMemory spike — a tracked zone `Publish()` yields `State=Modified`, `Property(Status).IsModified=True`.]

## Evidence: catalog filter URLs (Q5)

| Endpoint | Filter params (all GUID or enum) | Evidence |
|---|---|---|
| `GET /api/CoffeeShops` | `cityId: Guid?`, `q`, `roasters/equipments/beans/brewMethods/tags: Guid[]`, `isOpen/isNew/isVisited`, `priceRange`, `type` (enums), `minRating`, `page`, `pageSize` | `CoffeeShopsController.cs:33-48` → `SearchCoffeeShopsQuery.cs` |
| `GET /api/Map` | `minLat/minLon/maxLat/maxLon`, `zoom` only. No city/zone/tag filter | `MapController.cs:41-47` |
| Search cache | Hash built from GUIDs (`SearchCoffeeShopsHandler.CreateSearchHash`), key `shop:search:v3:*` | `CacheKey.cs:91-95` |

Slug-based params (for example `?city=minsk&tags=laptop_friendly`) could technically be added as new optional query params without breaking old clients. They are **not recommended in this phase**, for four reasons:
1. The handler must resolve slugs before `CreateSearchHash`, which changes the `SearchCoffeeShopsHandler` dependencies and forces Wolverine codegen.
2. Unknown-slug semantics (400, empty or ignore) are a product decision.
3. Two parameters for the same filter need precedence rules.
4. The frontend already has everything it needs: `GET /api/Catalogs/shop-tags` returns `{id, slug}`, and the new city/zone batch and by-slug endpoints plus the phase 2 roaster endpoints map slug ↔ GUID.

Enum filters (`type`, `priceRange`) already accept readable names through standard ASP.NET Core enum model binding [ASSUMED]. Document the stable filter values in `docs/api/public-addresses.md` instead (Open Question 4).

## Architecture Patterns

### System Architecture Diagram

```
                    ┌──────────── writes (admin) ────────────┐
Admin → Gateway ──► AdminCitiesController / AdminCoffeeZonesController
 (Moderator route)       │ Wolverine handlers (unchanged)
                         ▼
                 UnitOfWork.SaveChangesAsync
                         ▼
          ShopsDbContext.SaveChangesAsync hook ──(no owner change)──► base save
                         │ owner change: City Added/Deleted, Zone newly Published/Deleted
                         ▼
          PublicAddressTransaction.RunAsync (savepoint | own tx, pg_advisory_xact_lock)
                         ▼
          PublicAddressWriter.PrepareOwnerChangesAsync
            ├─ Deleted → RetireAsync<T> (always)
            └─ enabled? Added City / Published Zone → InitializeAsync<T> → FindCandidateAsync<T> (PublicSlug)
                         ▼
          base.SaveChangesAsync ──► PostgreSQL: CityPublicAddresses / CoffeeZonePublicAddresses
                                    (UNIQUE Slug, UNIQUE OwnerId WHERE State=1, FK RESTRICT) + PublicAddressAudit

                    ┌──────────── reads (public) ────────────┐
Frontend → Gateway /api/Catalogs/** ──► PublicCityAddressesController / PublicCoffeeZoneAddressesController
                         ▼ (503 if disabled)
          PublicAddressService.ResolveAsync(kind, slug)
            slug row → owner → IsVisibleAsync (City exists | Zone Published) → current canonical
                         ▼ null → 404
          data read (City row → CityDto; ICoffeeZoneQueries.GetByIdAsync → MapCoffeeZoneDto, re-check Published)
                         ▼
          PublicAddressEnvelope<T>(data, {entityId, slug, canonicalPath:"/cities/..|/coffee-zones/..", revision, isAlias})

Admin → /api/admin/{cities|coffee-zones}/public-addresses/... ──► AdminPublicAddressesController (Admin policy)
            initialize / rename(ExpectedRevision) / history / backfill(dryRun, cursor)
```

### Recommended Project Structure (new/changed files only)

```
CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/
├── CityPublicAddress.cs              # new, clone of CoffeeShopPublicAddress
└── CoffeeZonePublicAddress.cs        # new
CoffeePeek.Shops.Application/PublicAddresses/IPublicAddressService.cs   # enum +City=3,+CoffeeZone=4
CoffeePeek.Shops.Domain/.../Repositories/IQueryCityRepository.cs        # + GetById (AsNoTracking) for by-slug data
CoffeePeek.Shops.Persistance/
├── Configuration/PublicAddressConfiguration.cs      # + 2 config classes
├── Configuration/PublicAddressAuditConfiguration.cs # Kind IN (1,2,3,4)
├── Configuration/ShopsDbContext.cs                  # DbSets, ApplyConfiguration, ownerChanges gate
├── PublicAddresses/PublicAddressWriter.cs           # hook branches, Create<T> switch
├── PublicAddresses/PublicAddressService.cs          # 4-arm switches
├── Repositories/QueryCityRepository.cs              # GetById
└── Migrations/<ts>_AddCityAndZonePublicAddresses.cs (+Designer, snapshot)  # via make mig-shops
CoffeePeek.ShopsService/Controllers/
├── PublicCityAddressesController.cs       # [Route("api/Catalogs/cities")]
├── PublicCoffeeZoneAddressesController.cs # [Route("api/Catalogs/coffee-zones")]
└── AdminPublicAddressesController.cs      # regex + Kind switch
docs/api/public-addresses.md               # new kinds, paths, filter-value guidance, gate
Tests: Shops.Infrastructure.Tests/Persistence/{PublicAddressPersistenceTests,PublicAddressServiceTests or CityZonePublicAddressServiceTests}.cs,
       ShopsService.Tests/PublicAddressApiContractTests.cs, Shops.Domain.Tests/PublicAddresses/{PublicSlugTests,PublicAddressTests}.cs
```

### Pattern 1: Explicit kind switch (replace every ternary)
```csharp
// Source: pattern derived from PublicAddressService.cs:13-21 (phase 2)
public Task<PublicAddressMetadata?> ResolveAsync(PublicAddressKind kind, string slug, CancellationToken ct)
{
    RequireEnabled(kind);
    var normalized = slug.ToLowerInvariant();
    if (!PublicSlug.IsValid(normalized)) throw new PublicAddressException("Invalid public address.", 400);
    return kind switch
    {
        PublicAddressKind.CoffeeShop => ResolveAsync<CoffeeShopPublicAddress>(kind, normalized, ct),
        PublicAddressKind.Roaster => ResolveAsync<RoasterPublicAddress>(kind, normalized, ct),
        PublicAddressKind.City => ResolveAsync<CityPublicAddress>(kind, normalized, ct),
        PublicAddressKind.CoffeeZone => ResolveAsync<CoffeeZonePublicAddress>(kind, normalized, ct),
        _ => throw new PublicAddressException("Unknown address kind.", 400)
    };
}

private Task<bool> IsVisibleAsync(PublicAddressKind kind, Guid id, CancellationToken ct) => kind switch
{
    PublicAddressKind.CoffeeShop => db.Shops.AsNoTracking().AnyAsync(s => s.Id == id && !s.IsHidden, ct),
    PublicAddressKind.Roaster => db.Roasters.AsNoTracking().AnyAsync(s => s.Id == id, ct),
    PublicAddressKind.City => db.Cities.AsNoTracking().AnyAsync(c => c.Id == id, ct), // matches GET /api/Catalogs/cities
    PublicAddressKind.CoffeeZone => db.CoffeeZones.AsNoTracking()
        .AnyAsync(z => z.Id == id && z.Status == CoffeeZoneStatus.Published, ct),   // matches /api/Map
    _ => throw new PublicAddressException("Unknown address kind.", 400)
};
```

### Pattern 2: SaveChanges hook extension (no handler changes)
```csharp
// Source: extends ShopsDbContext.cs:30-44 and PublicAddressWriter.cs:12-27
internal static bool IsNewlyPublished(EntityEntry entry) =>
    entry.Entity is CoffeeZone { Status: CoffeeZoneStatus.Published } &&
    (entry.State == EntityState.Added ||
     (entry.State == EntityState.Modified && entry.Property(nameof(CoffeeZone.Status)).IsModified));

// ShopsDbContext ownerChanges gate:
var ownerChanges = ChangeTracker.Entries().Any(e =>
    (e.Entity is CoffeeShop or Roaster or City or CoffeeZone && e.State == EntityState.Deleted) ||
    (PublicAddressesEnabled && e.Entity is CoffeeShop or Roaster or City && e.State == EntityState.Added) ||
    (PublicAddressesEnabled && PublicAddressWriter.IsNewlyPublished(e)));

// PrepareOwnerChangesAsync additions: snapshot entries first (as existing code does), then
// Deleted City/Zone → RetireAsync<CityPublicAddress|CoffeeZonePublicAddress>(...)   (always)
// if (!db.PublicAddressesEnabled) return;
// Added City → InitializeAsync<CityPublicAddress>(db, PublicAddressKind.City, id, city.Name, null, "Automatically allocated on creation", ct)
// newly Published Zone → InitializeAsync<CoffeeZonePublicAddress>(db, PublicAddressKind.CoffeeZone, id, zone.Name, null, "Automatically allocated on publication", ct)
```

### Pattern 3: Public endpoints under the already-routed `api/Catalogs` prefix
```csharp
// Source: mirrors PublicRoasterAddressesController.cs:11-36
[ApiController]
[Route("api/Catalogs/coffee-zones")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicCoffeeZoneAddressesController(IPublicAddressService addresses, ICoffeeZoneQueries zones) : ControllerBase
{
    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(PublicAddressKind.CoffeeZone, slug, ct);
        if (address is null) return NotFound();
        var zone = await zones.GetByIdAsync(address.EntityId, ct);
        // Re-check status on the data read: never return a zone archived between resolve and read.
        if (zone is null || zone.Status != CoffeeZoneStatus.Published) return NotFound();
        return Ok(new PublicAddressEnvelope<MapCoffeeZoneDto>(new MapCoffeeZoneDto(zone.Id, zone.Name, zone.Description,
            zone.CenterLatitude, zone.CenterLongitude, zone.RadiusMeters, zone.ShopCount,
            zone.Polygon.Select(p => new MapCoffeeZonePointDto(p.Latitude, p.Longitude)).ToArray()), address));
    }
    [HttpGet("public-addresses")] /* GetByIdsAsync(CoffeeZone, ids) */
    [HttpGet("{id:guid}/public-address")] /* single legacy GUID */
}
// City equivalent: [Route("api/Catalogs/cities")], data = CityDto { Id, Name } from IQueryCityRepository.GetById (new, AsNoTracking).
```
Route safety: `api/Catalogs/cities` + `by-slug/{slug}` does not collide with `CatalogsController`'s `GET api/Catalogs/cities` (`CatalogsController.cs:19`). Gateway `shops-Catalogs-route` `/api/Catalogs/{**remainder}` (`CoffeePeek.Gateway/appsettings.json:188-191`) already forwards it. Global limiter 300/min applies (`RateLimitingExtensions.cs:38-39`).

### Pattern 4: Admin route extension
```csharp
// Source: AdminPublicAddressesController.cs:11,39-40
[Route("api/admin/{entity:regex(^(shops|roasters|cities|coffee-zones)$)}/public-addresses")]
private static PublicAddressKind Kind(string entity) => entity.ToLowerInvariant() switch
{
    "shops" => PublicAddressKind.CoffeeShop,
    "roasters" => PublicAddressKind.Roaster,
    "cities" => PublicAddressKind.City,
    "coffee-zones" => PublicAddressKind.CoffeeZone,
    _ => throw new PublicAddressException("Unknown address kind.", 400)
};
```
No route collisions: `api/admin/cities/public-addresses/{id:guid}` vs `AdminCitiesController` `api/admin/cities/{id:guid}` (different segment count); `api/admin/coffee-zones/public-addresses/backfill` vs `.../candidates` (different literal). Gateway routes `/api/admin/cities/**` and `/api/admin/coffee-zones/**` require Moderator (`appsettings.json:115-118,140-143`), and the controller still enforces `RoleConsts.Admin`.

### Anti-Patterns to Avoid
- **Generic "owner descriptor" strategy/interface layer:** speculative; four explicit switch arms are shorter and match the project's "shortest working diff" preference.
- **Adding a slug column to `City`/`CoffeeZone` or `CityDto`:** bypasses the history, alias and tombstone model and changes a Contract DTO.
- **Migrating ShopTag slugs to hyphens or into a registry:** breaks `ImportCandidatePublishedEvent.TagSlugs` and Moderation's stored `TagSlugs` (`ShopImportCandidate.cs:43`).
- **Retiring zone addresses on archive:** retirement is irreversible, but archive is reversible.
- **Allocating in Wolverine handlers:** changes handler signatures, forces codegen, and misses other write paths. Use the SaveChanges hook.
- **Putting canonical metadata into `city:list:all` or map payloads:** creates cache invalidation obligations; phase 2 deliberately avoided this (`docs/api/public-addresses.md:41`).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Slugs/transliteration | New city/zone slug rules | `PublicSlug.Generate/Candidate/IsValid` | Rules are versioned; must not diverge |
| Collision handling | New suffix logic | `PublicAddressWriter.FindCandidateAsync<T>` | Bounded 8/12/16-hex deterministic |
| Transactions/locking | New transaction code | `PublicAddressTransaction.RunAsync` | Ambient savepoint + advisory lock + tracker clear on rollback |
| Rename/restore/history | Per-kind copies | Generic `ChangeAsync<T>`, `HistoryAsync`, audit table | A→B→A and owner-wide revision already tested |
| Backfill | New admin endpoints | Existing `backfill` route with new `entity` values | Cursor/dry-run/replay semantics already tested |
| Slug→GUID for search | Search param parsing | Existing by-slug/batch endpoints | Avoids codegen and cache-hash changes |

**Key insight:** every invariant (at most one canonical, tombstone reservation, rollback) already lives in shared generic code. The risk in this phase is **missed dispatch arms and schema checks**, not missing logic.

## Runtime State Inventory

(Not a rename phase, but the phase adds registries that existing production rows must be backfilled into.)

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | Existing `Cities` rows (admin-created; no seeds) and existing Published `CoffeeZones` will have no addresses after migration | Backfill via `POST api/admin/{cities|coffee-zones}/public-addresses/backfill?dryRun=true` → review → write. Operational, not in this phase |
| Live service config | `PublicAddresses:Enabled` is set in no appsettings or deploy file (grep: no `PublicAddresses` in `*.json`, `deploy/*`) → defaults false (`IPublicAddressService.cs:8-12`) | None; stays default-off |
| OS-registered state | None: verified by searching for scheduled tasks/cron in `deploy/` (only `update.sh` hits `/api/Catalogs/cities` for health) | None |
| Secrets/env vars | Design-time EF factory needs `PostgresCpOptions__ConnectionString` (`ShopsDbContextFactory.cs:20-25`), even for `migrations add` | Set a dummy value when generating the migration (see Pitfall 7) |
| Build artifacts | Wolverine `Internal/Generated/WolverineHandlers/` is affected only if handler signatures change | None with the recommended design; if a handler changes, run `dotnet run -- codegen write` |

## Common Pitfalls

### Pitfall 1: Audit kind CHECK constraint
**What goes wrong:** the first city or zone initialize/rename/retire fails with a check violation on `PublicAddressAudit`.
**Why:** `CK_PublicAddressAudit_Kind` is `"Kind" IN (1, 2)` (`PublicAddressAuditConfiguration.cs:19`; migration `20260928072406_AddPublicAddressAudit.cs:32`). InMemory ignores check constraints, so the tests stay green.
**How to avoid:** change the config to `IN (1, 2, 3, 4)`. Verify the generated migration drops and re-adds the constraint, and assert the SQL in a model test.
**Warning signs:** the migration contains only `CreateTable` calls.

### Pitfall 2: Silent default-to-Roaster dispatch
**What goes wrong:** a City is resolved against `RoasterPublicAddresses`, created as `RoasterPublicAddress` (InvalidCast), or the admin route maps `cities` → Roaster.
**Why:** the ternaries in the 14-row table above.
**How to avoid:** use switches with `_ => throw`. Add a test per kind that exercises resolve, batch, change, history and backfill, plus a reflection or route test that the admin regex set equals the `Kind()` switch set.

### Pitfall 3: EF InMemory does not round-trip `CoffeeZone.Polygon`
**What goes wrong:** in InMemory, a re-queried CoffeeZone has `Polygon == null`. Saving a modification to it throws `The complex type property 'CoffeeZone.Polygon' is configured as required (non-nullable) but has a null value`. `ICoffeeZoneQueries.GetByIdAsync` → `ToDto` would NRE on `zone.Polygon.ToArray()` (`CoffeeZoneQueries.cs:148-151`).
**Why:** `ComplexCollection(...).ToJson()` (`CoffeeZoneConfiguration.cs:28`). EF docs list in-memory complex-type support as a known gap [CITED: github.com/dotnet/entityframework.docs ef-core-8.0 whatsnew "Implement complex types for the in-memory database (#31464)"]. [VERIFIED: spike on EF 10.0.5 in this session.]
**How to avoid:** in InMemory tests, publish the **same tracked instance** you added (the spike showed that works and `IsModified` is detected). Service code should project zone columns with `Select(z => z.Name)` / `Select(z => z.Id)`. Do not unit-test the zone by-slug data path against InMemory; cover it with route/contract tests plus the live gate.

### Pitfall 4: InMemory cannot prove FK Restrict, uniqueness, checks, rollback or the advisory lock
**What goes wrong:** you conclude that "city with zones cannot be deleted and its addresses stay live" works.
**Why:** `PublicAddressTransaction` skips transactions for non-relational providers (`PublicAddressTransaction.cs:11`). InMemory enforces no FK or unique constraints. With both entities tracked it instead throws a client-side "association severed" error [VERIFIED: spike].
**How to avoid:** add explicit live-PG gate items (see Validation Architecture). Keep `Enabled=false`.

### Pitfall 5: Reserved-word and transliteration surprises in city names
**Facts** [VERIFIED: spike running `PublicSlug.Generate`]: `Минск`/`Мінск` → `minsk`; `Гродно` → `grodno` vs `Гродна` → `grodna`; `Могилёв` → `mogilyov` vs `Магілёў` → `magilyou`; `Витебск` → `vitebsk` vs `Віцебск` → `vitsebsk`; `Брест`/`Брэст` → `brest`; `Верхний город` → `verkhniy-gorod`; zone names `New`/`Search`/`Me` → `place`. BY `г` → `g` (not the official BY romanization `h`).
**How to avoid:** do not change `PublicSlug` rules in this phase. Rely on dry-run backfill review and manual `initialize` with a chosen slug for the handful of cities. Add these as regression cases in `PublicSlugTests` so generation stays stable.
**Also:** `cities`, `coffee-zones` and `map` are *valid* slugs (not reserved). That is harmless because canonical paths are prefixed. If the frontend ever mounts cities at the top level (`/{city}`), reserved words must be added **before** backfill. It is safe now because no addresses exist in production.

### Pitfall 6: Duplicate city names
`City.Name` has no unique index (snapshot `:290-310`). Only a read-then-insert check exists (`AdminCityHandlers.cs:25-27`). Two "Минск" rows get `minsk` and `minsk-<8hex>` in owner-GUID backfill order. Review the dry-run output.

### Pitfall 7: Migration generation environment
`make mig-shops n=AddCityAndZonePublicAddresses` invokes the design-time factory, which throws without `PostgresCpOptions__ConnectionString` (`ShopsDbContextFactory.cs:20-25`) [VERIFIED: ran `has-pending-model-changes` without it → error; with a dummy value → "No changes have been made to the model since the last migration."]. No DB connection is needed. The baseline is clean, so any unrelated diff in the new migration or snapshot is drift and must be investigated.

### Pitfall 8: `ShopsService.Tests` does not run in CI
`CoffeePeek.Backend.ci.slnf` omits `CoffeePeek.ShopsService.Tests` (CI runs `dotnet test CoffeePeek.Backend.ci.slnf`, `.github/workflows/ci-cd.yml:70`). The phase 2 route/contract tests and the Wolverine codegen guard are therefore not CI-enforced. Run them locally in every verification step. Adding the project to the slnf is a one-line decision for the user (Open Question 5).

### Pitfall 9: Search visibility vs address visibility mismatch (pre-existing, phase 2)
Shop address visibility uses `!IsHidden` only (`PublicAddressService.cs:155`), while search, map and zone counts use `Status == Active && !IsHidden` (`CoffeeShopQueries.cs:30-31`). This does not affect cities or zones. Note it, and do not fix it silently in this phase.

## Code Examples

### Typed registry configuration (new)
```csharp
// Source: clone of PublicAddressConfiguration.cs:10-17
public sealed class CityPublicAddressConfiguration : IEntityTypeConfiguration<CityPublicAddress>
{
    public void Configure(EntityTypeBuilder<CityPublicAddress> builder)
    {
        PublicAddressConfiguration.Configure(builder, "CityPublicAddresses");
        builder.HasOne<City>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class CoffeeZonePublicAddressConfiguration : IEntityTypeConfiguration<CoffeeZonePublicAddress>
{
    public void Configure(EntityTypeBuilder<CoffeeZonePublicAddress> builder)
    {
        PublicAddressConfiguration.Configure(builder, "CoffeeZonePublicAddresses");
        builder.HasOne<CoffeeZone>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
```
Generated identifier lengths stay under PostgreSQL's 63-char limit (longest: `FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId`, 48 chars).

### Backfill owner listing (verified SQL)
```csharp
// Npgsql translation verified via ToQueryString in this session:
// SELECT c."Id", c."Name" FROM "CoffeeZones" AS c WHERE c."Status" = 1 AND c."Id" > @afterId ORDER BY c."Id" LIMIT @p
PublicAddressKind.CoffeeZone => await db.CoffeeZones.AsNoTracking()
    .Where(z => z.Status == CoffeeZoneStatus.Published && (!afterId.HasValue || z.Id.CompareTo(afterId.Value) > 0))
    .OrderBy(z => z.Id).Take(limit + 1).Select(z => new OwnerStem(z.Id, z.Name, null)).ToArrayAsync(ct),
```

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| Binary shop/roaster dispatch (phase 2) | Explicit 4-kind switch | Adding kinds is compile-visible, with no silent defaults |
| Wolverine 5.30 (CLAUDE.md) | WolverineFx 6.35.0 (`Directory.Packages.props:28`) | Doc drift only; no impact here |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Enum query params (`type=Specialty`, `priceRange=...`) already bind by name in ASP.NET Core | Filter URLs | Low. The frontend can use numeric values; documentation only |
| A2 | The frontend (external) will compose filter URLs from catalog slugs and resolve via batch/by-slug rather than needing server-side slug params | Filter URLs | Medium. If false, add an additive `city`/`tag` slug param later (requires codegen) |
| A3 | A zone page needs only the zone's map DTO data (no member shop list, no city) from by-slug | Pattern 3 | Low. A richer public zone DTO is additive |
| A4 | The low admin write volume makes the single global advisory lock across four kinds acceptable | Architecture | Low |

## Open Questions (RESOLVED — see 03-CONTEXT.md)

1. **Zone URL shape.** Recommended: global per-type zone namespace, canonical `/coffee-zones/{slug}` (spec-consistent, zero registry change). The alternative, nested `/cities/{city}/coffee-zones/{zone}` with per-city uniqueness, forks every generic helper (see the Q3 evidence section). **Decide before planning.** **RESOLVED: D-01 (global).**
2. **City visibility.** Recommended: every existing city is public, matching `GET /api/Catalogs/cities`. The alternative, only cities with ≥1 active visible shop, is a new product rule and would also apply to the batch endpoint. **RESOLVED: D-02 (all cities).**
3. **Catalog scope.** Recommended: City + CoffeeZone registries only. ShopTag uses its existing immutable slug. BrewMethod/CoffeeBean/Equipment are deferred. Confirm that no page URLs for tags or brew methods are wanted. **RESOLVED: D-03.**
4. **Search slug params.** Recommended: none this phase; the frontend resolves slug → GUID. Confirm. **RESOLVED: D-04 (none).**
5. **CI coverage.** Should `CoffeePeek.ShopsService.Tests` be added to `CoffeePeek.Backend.ci.slnf` so route and codegen contract tests run in CI? This research does not assume it. **RESOLVED: D-05 (add).**
6. **Path prefixes.** `/cities/{slug}` and `/coffee-zones/{slug}` are proposed; the external frontend owns the actual page routes. **RESOLVED: D-01.**

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test | ✓ | 10.0.400 | — |
| dotnet-ef | migration | ✓ | 10.0.5 | — |
| make | `make mig-shops` | ✓ | chocolatey make | manual `dotnet ef migrations add ... --project CoffeePeek.Shops.Persistance --startup-project CoffeePeek.ShopsService --context CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext --output-dir Migrations` |
| Docker daemon | live PG gate | ✗ (`//./pipe/dockerDesktopLinuxEngine` missing) | CLI 28.5.2 | None. Live gate stays open; feature stays disabled |
| psql | live PG gate | ✗ | — | None |

**Missing with no fallback:** live PostgreSQL. This blocks only enablement, not implementation.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 + FluentAssertions + Moq; EF InMemory 10.0.5 for behavior; Npgsql design-time model for schema assertions |
| Config file | none (csproj `<Using Include="Xunit"/>`) |
| Quick run command | `dotnet test CoffeePeek.Shops.Infrastructure.Tests/CoffeePeek.Shops.Infrastructure.Tests.csproj --filter "FullyQualifiedName~PublicAddress"` (baseline **6 passed**, verified this session) |
| Contract run | `dotnet test CoffeePeek.ShopsService.Tests/CoffeePeek.ShopsService.Tests.csproj` (baseline **11 passed**, verified; **not in CI slnf**) |
| Full suite command | `dotnet test CoffeePeek.Shops.Domain.Tests` + `CoffeePeek.Shops.Application.Tests` + `CoffeePeek.Shops.Infrastructure.Tests` + `CoffeePeek.ShopsService.Tests`; then `dotnet build CoffeePeek.Backend.ci.slnf -c Debug --no-restore` |
| Schema check | `PostgresCpOptions__ConnectionString="Host=localhost;Database=unused;Username=u;Password=p" dotnet ef migrations has-pending-model-changes --project CoffeePeek.Shops.Persistance/CoffeePeek.Shops.Persistance.csproj --startup-project CoffeePeek.ShopsService/CoffeePeek.ShopsService.csproj --context CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext` (baseline clean) plus `dotnet ef migrations script 20260928072406_AddPublicAddressAudit <new>` for SQL review |

### Behavior → Test Map
| Label | Behavior | Test Type | Where | Exists? |
|--------|----------|-----------|-------|---------|
| P3-01 | Added City (enabled) → `minsk`; resolve `MINSK` is canonical, not alias; display-name `Update` keeps slug | InMemory service | new `CityZonePublicAddressServiceTests.cs` (or extend `PublicAddressServiceTests.cs`) | ❌ Wave 0 |
| P3-01 | City rename A→B→A, history (initialize, rename, rename), Kind=City audit | InMemory | same | ❌ |
| P3-01 | City delete → all rows tombstoned, `OwnerId` null, revision+1; resolve null; new city with same name gets suffixed slug | InMemory | same | ❌ |
| P3-02 | Zone created Draft → no rows; `Publish()` on tracked instance → `oktyabrskaya`; Archive/MoveToDraft → resolve null, batch empty; republish → same row, no new revision | InMemory | same | ❌ |
| P3-02 | Draft zone admin initialize with manual slug, then publish keeps it | InMemory | same | ❌ |
| P3-03 | City/zone namespaces independent of each other and of shops (`minsk` in all three) | InMemory | same | ❌ |
| P3-04 | Backfill cities (all) and zones (Published only): dry-run no writes, collision flag, cursor, replay idempotent | InMemory | same | ❌ |
| P3-06 | Disabled: create city/publish zone → zero rows; service calls → 503 | InMemory | same | ❌ |
| P3-06 | Existing shop/roaster tests unchanged and green | InMemory | `PublicAddressServiceTests.cs` | ✅ |
| schema | `CityPublicAddresses`/`CoffeeZonePublicAddresses`: table, unique Slug (no filter), filtered canonical owner index, Restrict FK, 4 checks, C collation | Npgsql model | extend `PublicAddressPersistenceTests.cs` `[InlineData]` | ✅ extend |
| schema | Audit `CK_PublicAddressAudit_Kind` SQL contains `3, 4` | Npgsql model | new fact in `PublicAddressPersistenceTests.cs` | ❌ |
| domain | Lifecycle theory covers the new types; city/zone translit regression cases | unit | `PublicAddressTests.cs`, `PublicSlugTests.cs` | ✅ extend |
| API | Routes `api/Catalogs/cities` and `api/Catalogs/coffee-zones` + `by-slug/{slug}`, `public-addresses`, `{id:guid}/public-address`; NoStore on both; admin regex includes `cities|coffee-zones` and Admin policy; existing `CatalogsController.GetCities` template unchanged (`cities`) | reflection | extend `PublicAddressApiContractTests.cs` | ✅ extend |
| codegen | No handler drift | host start | `WolverinePreGeneratedHandlersTests.cs` (must stay green) | ✅ |

### What cannot be proven without live PostgreSQL (extend `docs/api/public-addresses.md` gate)
- Unique `Slug` and filtered-canonical indexes on the two new tables under concurrent allocation.
- `FK_CityPublicAddresses_Cities_OwnerId` / `FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId` RESTRICT; deleting a city **without** retirement fails.
- City delete while zones exist → 409, **and** the retirement rows roll back (savepoint/transaction).
- `CK_PublicAddressAudit_Kind` accepts 3 and 4; `CK_*_Lifecycle` rejects a Canonical row without an owner.
- Advisory-lock serialization for two concurrent "Минск" creations.

### Sampling Rate
- **Per task commit:** quick run + contract run.
- **Per wave merge:** full suite + CI slnf build + `has-pending-model-changes`.
- **Phase gate:** all green; live-PG items listed as open; `PublicAddresses__Enabled` default stays false.

### Wave 0 Gaps
- [ ] New InMemory test file for city/zone lifecycle (use the tracked-instance zone pattern; see Pitfall 3).
- [ ] New `[InlineData]` rows and audit-check fact in `PublicAddressPersistenceTests.cs`.
- [ ] Contract test cases for new controllers and the admin regex/Kind mapping.

## Security Domain

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no (reuses gateway JWT) | — |
| V3 Session Management | no | — |
| V4 Access Control | yes | `[Authorize(Policy = RoleConsts.Admin)]` on `AdminPublicAddressesController`; the gateway Moderator routes for cities/zones are weaker, so the service policy is the authority |
| V5 Input Validation | yes | `PublicSlug.IsValid` (strict, `\A...\z`, reserved, GUID-shaped rejected), batch 1–100 non-empty GUIDs, route regex for entity, reason 1–500 chars |
| V6 Cryptography | no | — |
| V8 Data Protection / info leakage | yes | Draft/Archived zones, unknown slugs and tombstones all return an identical 404; batch returns Published zones only (no draft enumeration); `NoStore` response caching |

### Known Threat Patterns
| Pattern | STRIDE | Mitigation |
|---------|--------|------------|
| Enumerating draft zones via batch/GUID endpoint | Information disclosure | Visibility filter `Status == Published` in batch and resolve; re-check on data read |
| Open redirect via canonicalPath | Tampering | Server-built fixed prefixes only (`Metadata`); never echo request input |
| Slug squatting by a Moderator | Elevation | Admin-only initialize/rename; audit actor/reason |
| Race on rename → 500 leaking internals | DoS/Info | `IsAddressConflict` recognizes the new index prefixes → 409 |
| Cache poisoning of address data | Tampering | Address reads bypass Redis; `NoStore` |

## Sources

### Primary (HIGH confidence)
- Codebase (file:line cited throughout): `PublicAddressService.cs`, `PublicAddressWriter.cs`, `PublicAddressTransaction.cs`, `PublicAddressConfiguration.cs`, `PublicAddressAuditConfiguration.cs`, `ShopsDbContext.cs`, `AdminPublicAddressesController.cs`, `City.cs`, `CoffeeZone.cs`, `ShopTag.cs`, `CoffeeZoneMessages.cs`, `AdminCityHandlers.cs`, `CoffeeShopQueries.cs`, `CoffeeShopsController.cs`, `MapController.cs`, `CatalogsController.cs`, `CacheKey.cs`, `ShopsDbContextModelSnapshot.cs`, `CoffeePeek.Gateway/appsettings.json`, `CoffeePeek.Backend.ci.slnf`, `.github/workflows/ci-cd.yml`.
- Specs: `.planning/specs/PUBLIC-SLUGS.md`, `.planning/specs/SHOP-FILTERS-TAGS.md`, `docs/api/public-addresses.md`, `docs/map-search-client-contract.md`, phase 1/2 SUMMARY/VERIFICATION.
- Session spikes (throwaway, outside the repo, deleted): InMemory zone publish/reload/city-delete behavior; `PublicSlug.Generate` outputs; Npgsql `ToQueryString` for cursor queries; `has-pending-model-changes` baseline; focused test baselines (6 infra and 11 service tests passed).
- Context7 `/dotnet/entityframework.docs`: complex types and ToJson, in-memory provider limitations (transactions, complex types #31464).

### Secondary / Tertiary
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH (no new packages; versions read from props).
- Architecture / extension points: HIGH (every dispatch site enumerated with line numbers; SQL and InMemory behavior verified by spikes).
- Product-shape recommendations (zone nesting, city visibility, catalog scope): MEDIUM (evidence-based, but they are user decisions).
- Pitfalls: HIGH (audit check, InMemory Polygon, CI omission, design-time env var all verified in this session).

**Research date:** 2026-09-28
**Valid until:** 2026-10-28 (stable internal codebase; re-check if phase 2 files change)
