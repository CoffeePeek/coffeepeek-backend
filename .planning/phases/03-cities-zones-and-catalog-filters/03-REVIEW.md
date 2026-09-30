---
phase: 03-cities-zones-and-catalog-filters
reviewed: 2026-09-28T09:02:34Z
depth: standard
files_reviewed: 22
files_reviewed_list:
  - CoffeePeek.Backend.ci.slnf
  - CoffeePeek.Shops.Application/PublicAddresses/IPublicAddressService.cs
  - CoffeePeek.Shops.Domain.Tests/PublicAddresses/PublicAddressTests.cs
  - CoffeePeek.Shops.Domain.Tests/PublicAddresses/PublicSlugTests.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IQueryCityRepository.cs
  - CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CityPublicAddress.cs
  - CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CoffeeZonePublicAddress.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Persistence/CityZonePublicAddressServiceTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Persistence/PublicAddressPersistenceTests.cs
  - CoffeePeek.Shops.Persistance/Configuration/PublicAddressAuditConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/PublicAddressConfiguration.cs
  - CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs
  - CoffeePeek.Shops.Persistance/Migrations/20260928082712_AddCityAndZonePublicAddresses.cs
  - CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs
  - CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressWriter.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryCityRepository.cs
  - CoffeePeek.ShopsService.Tests/PublicAddressApiContractTests.cs
  - CoffeePeek.ShopsService.Tests/PublicCatalogAddressControllerTests.cs
  - CoffeePeek.ShopsService/Controllers/AdminPublicAddressesController.cs
  - CoffeePeek.ShopsService/Controllers/PublicCityAddressesController.cs
  - CoffeePeek.ShopsService/Controllers/PublicCoffeeZoneAddressesController.cs
  - docs/api/public-addresses.md
findings:
  critical: 0
  warning: 4
  info: 5
  total: 9
status: issues_found
---

# Phase 3: Code Review Report

**Reviewed:** 2026-09-28T09:02:34Z
**Depth:** standard
**Files Reviewed:** 22
**Status:** issues_found

## Summary

I reviewed the phase 3 diff (`d812e0c0..HEAD`): the City/CoffeeZone registries, the migration, the SaveChanges lifecycle hook, the four-kind dispatch, the public and admin controllers, the tests and the runbook. I also traced the paths the code relies on but that sit outside the file list: `DeleteCityHandler`, `UnitOfWork<T>` (maps `DbUpdateException` to `ConflictException` / 409), the Wolverine-generated `DeleteCityCommandHandler`, `CoffeeZoneQueries`, `CoffeeShopQueries.GetMap`, the gateway `ReverseProxy` routes, the ShopsService authorization policies and `deploy/scripts/apply-migrations.sh`.

I ran two suites locally, both in Debug:
- `CoffeePeek.Shops.Infrastructure.Tests` filtered to `PublicAddress|CityZone`: 17/17 pass.
- The whole `CoffeePeek.ShopsService.Tests` project, newly added to CI: 25/25 pass.

**Verified correct, no finding:**
- **Visibility.** by-slug, batch and by-id for zones all filter on `Status == Published` (`IsVisibleAsync` / `VisibleIdsAsync`). The zone by-slug endpoint re-checks status on the data read. History is reachable only through the Admin-policy controller: the gateway `Moderator` routes for `/api/admin/cities/**` and `/api/admin/coffee-zones/**` still hit `[Authorize(Policy = Admin)]` in ShopsService. The zone `ShopCount` uses the same active and non-hidden shop filter as `/api/Map`. I found no Draft/Archived leak.
- **Four-kind dispatch.** Every switch in `PublicAddressService`, `PublicAddressWriter.Create<T>`, `IsAddressConflict`, `PrepareOwnerChangesAsync`, the `ShopsDbContext` gate and the admin `Kind()` lists all four kinds and throws on anything else. Nothing falls through silently.
- **Lifecycle hook.** A city delete retires its addresses before the `PublicAddressesEnabled` early return. A zone is allocated only on Added+Published or on a `Status` change to Published, and `InitializeAsync` is idempotent. The `is ... or ... && ...` precedence in `ShopsDbContext.cs:38-40` parses as intended.
- **Migration.** `Up` is correct. It drops and re-adds `CK_PublicAddressAudit_Kind` with `IN (1, 2, 3, 4)`, creates two tables with RESTRICT FKs, and creates six indexes whose names match the `IsAddressConflict` prefixes.

No BLOCKER-level defect could be proven. The main risks:
- One regression test cannot fail.
- Disabled mode still depends on the new tables.
- The city-delete-with-zones path is unguarded and untested.
- The public zone/city reads duplicate mapping and counting logic that already exists for `/api/Map`.

## Narrative Findings (AI reviewer)

## Warnings

### WR-01: "Display-name edit never renames" test passes vacuously; no zone equivalent exists

**File:** `CoffeePeek.Shops.Infrastructure.Tests/Persistence/CityZonePublicAddressServiceTests.cs:38-42`
**Issue:** The test creates `City("Минск")` and then calls `city.Update("Minsk")`. Both names produce the same slug: `PublicSlug.Generate("Минск") == PublicSlug.Generate("Minsk") == "minsk"`.

Suppose a regression re-allocated or renamed the address on every City `Modified`. It would compute `minsk` again and hit the `current.Slug == request.Slug` / idempotent `InitializeAsync` no-op. So `IsAlias == false` and `Count == 1` both still pass. The assertion's stated reason ("a display-name edit never renames the public address") is never actually tested.

Coffee zones have no equivalent at all. No test edits the name of a Published zone (`zone.Update(...)`) and asserts that no rename happens and no audit row is added. The same applies to editing a Published zone that has no address: it should not allocate, because only a `Status` transition should.
**Fix:**
```csharp
city.Update("Столица Беларуси");            // generates "stolitsa-belarusi", not "minsk"
await db.SaveChangesAsync(ct);
var afterEdit = await service.ResolveAsync(PublicAddressKind.City, "minsk", ct);
afterEdit!.IsAlias.Should().BeFalse();
afterEdit.Revision.Should().Be(1);
(await db.CityPublicAddresses.CountAsync(ct)).Should().Be(1);
(await db.PublicAddressAudit.CountAsync(a => a.OwnerId == city.Id, ct)).Should().Be(1);

// Zone: edit a Published zone (same tracked instance, InMemory-safe)
zone.Update(zone.CityId, "Nemiga Quarter", null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));
await db.SaveChangesAsync(ct);
(await service.ResolveAsync(PublicAddressKind.CoffeeZone, "nemiga", ct))!.Revision.Should().Be(1);
(await db.PublicAddressAudit.CountAsync(a => a.OwnerId == zone.Id, ct)).Should().Be(1);
```

### WR-02: Disabled mode still hard-depends on the new tables; runbook says GUID APIs are unchanged

**File:** `CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs:37-48`, `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressWriter.cs:24-29`, `docs/api/public-addresses.md:5-17`
**Issue:** Every tracked `City` delete enters `PublicAddressTransaction.RunAsync` whatever the feature flag says. It takes the global advisory lock and runs `RetireAsync<CityPublicAddress>`, which issues `SELECT ... FROM "CityPublicAddresses"` (`PublicAddressWriter.cs:96`).

This happens before the `if (!db.PublicAddressesEnabled) return;` at line 29. ShopsService does not migrate on startup: `ApplyMigrations` is never called, and migrations are applied by hand through `deploy/scripts/apply-migrations.sh`.

So suppose this build ships before `20260928082712_AddCityAndZonePublicAddresses` is applied, which is reasonable for a "default-off" feature. Then `DELETE api/admin/cities/{id}` fails with PostgreSQL `42P01` (relation does not exist). That is a query exception, not a `DbUpdateException`, so `UnitOfWork` does not map it and the response is a 500.

The runbook lists the migration under the *enablement* gate (line 7). Line 17 states that when disabled "existing GUID APIs are unchanged", which is false for city deletion. The same was already true of shop/roaster deletes after phase 2, and phase 3 widens it to cities.
**Fix:** Make the migration a deployment prerequisite in the runbook, not an enablement step:
```markdown
## Deployment prerequisite (independent of the flag)
Apply `20260927112559_AddPublicAddressRegistries`, `20260928072406_AddPublicAddressAudit` and
`20260928082712_AddCityAndZonePublicAddresses` **before** rolling out this build. Deletions of shops,
roasters and cities always retire addresses (even with `PublicAddresses__Enabled=false`) and fail
with HTTP 500 if these tables are missing.
```
Also correct line 17 accordingly. Optionally, add a startup check that fails fast when `GetPendingMigrationsAsync()` contains these migrations.

### WR-03: Deleting a city that still has zones relies on a DB FK failure mid-transaction; unguarded, misleading, untested

**File:** `CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs:44-48`, `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressWriter.cs:24-25`, `docs/api/public-addresses.md:11` (path: `CoffeePeek.Shops.Application/Features/Admin/Catalogs/Cities/AdminCityHandlers.cs:79-84`)
**Issue:** `DeleteCityHandler` does not check for zones. The delete goes through these steps:
1. The hook retires every city address and writes a `retire` audit row inside a savepoint.
2. `DELETE FROM "Cities"` then violates `FK_CoffeeZones_Cities_CityId` (RESTRICT).
3. `RunAsync` rolls back to the savepoint and calls `ChangeTracker.Clear()`.
4. `UnitOfWork` turns the error into a `ConflictException` whose message is "A database conflict occurred. The record may already exist."

The documented "409, and its address retirement rolls back" is therefore:
- only an accident of generic exception mapping;
- misleading to the admin (the city does not "already exist"; it has zones);
- completely unverified, because the InMemory tests explicitly avoid the scenario (header comment lines 17-18) and the live gate has not run.

A regression in the savepoint/rollback path would leave a live city with tombstoned addresses (OwnerId NULL, resolving to 404). No test would catch it.
**Fix:** Guard in the application layer, so the retirement is never attempted and the behavior is deterministic and InMemory-testable:
```csharp
// DeleteCityHandler.Handle, before repository.Remove(city)
if (await zoneRepository.AnyForCityAsync(command.Id, ct))
    return Response.Error((int)HttpStatusCode.Conflict,
        "City still has coffee zones. Archive is not enough; zones must be moved or removed first.");
```
Then add a handler test for it, plus an InMemory test asserting that the city's address stays Canonical at revision 1 after the rejected delete.

### WR-04: Public zone/city reads bypass CQRS and duplicate the `/api/Map` DTO mapping and ShopCount logic

**File:** `CoffeePeek.ShopsService/Controllers/PublicCoffeeZoneAddressesController.cs:25-31`, `CoffeePeek.ShopsService/Controllers/PublicCityAddressesController.cs:25-27`
**Issue:** Phase 2's public controllers load entity data through `IMessageBus` and the existing query handlers (`GetCoffeeShopQuery`, `GetRoasterByIdQuery`). The phase 3 controllers do something different:
- They call a Domain repository (`IQueryCityRepository`) directly.
- For zones they call the *admin* query `ICoffeeZoneQueries.GetByIdAsync`.
- They hand-map the result into `MapCoffeeZoneDto` / `CityDto` inside the controller.

The "same shape as `/api/Map` zones" contract (docs line 26) now depends on two independent code paths staying in sync:
- the mapping in `CoffeeShopQueries.GetMap` (lines 289-297) versus the controller;
- the member counting in `CoffeeZoneQueries.CountMembers` versus `CoffeeShopQueries.IsMember`.

The two counters are equivalent today only because `LoadActiveShopsAsync` and the map query happen to use the same filters. `ZoneBySlug_Published_ReturnsMapZoneEnvelope` compares against the admin DTO, not against `/api/Map` output, so drift would go unnoticed. The `CityDto` is also hand-built instead of using the Mapster mapping that `GET api/Catalogs/cities` uses.
**Fix:** Extract one mapping helper, for example `MapCoffeeZoneDtoFactory.From(CoffeeZone zone, int shopCount)`, and use it in both `GetMap` and the by-slug path. Expose a public query such as `ICoffeeZoneQueries.GetPublishedMapZoneAsync(id)` (filtered to Published in SQL, counting via the same `IsMember` as the map), or a Wolverine query handler with regenerated code. For cities, use `mapper.Map<CityDto>(city)`. Add a test asserting that the by-slug `data` equals the zone entry returned by `GetMap` for the same fixture.

## Info

### IN-01: Migration `Down` cannot run after any city/zone allocation

**File:** `CoffeePeek.Shops.Persistance/Migrations/20260928082712_AddCityAndZonePublicAddresses.cs:115-131`
**Issue:** `Down` drops both registries but keeps `PublicAddressAudit` rows with `Kind` 3/4. It then re-adds `"Kind" IN (1, 2)`, and PostgreSQL rejects that because existing rows are validated. Npgsql runs each migration in a transaction, so this fails atomically and loses no data, and the runbook (line 138) warns against it. Still, the failure an operator sees is an opaque check-constraint error.
**Fix:** Fail fast with a clear message, for example `migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"PublicAddressAudit\" WHERE \"Kind\" IN (3,4)) THEN RAISE EXCEPTION 'City/zone addresses were allocated; Down is not supported'; END IF; END $$;")` as the first `Down` operation.

### IN-02: Kind-to-registry pairing is repeated in about 12 places, and a mismatched pair compiles

**File:** `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs:20-27,45-52,70-77,130-137,167-181,185-193,195-204,206-214,220-227`, `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressWriter.cs:83-90`
**Issue:** `kind` and `T` are passed separately everywhere. A mistake such as `BackfillAsync<CityPublicAddress>(PublicAddressKind.CoffeeZone, ...)` compiles and would write audit rows under the wrong kind. Adding a fifth kind (BrewMethod, CoffeeBean, Equipment are deferred) means editing every switch.
**Fix:** Use one descriptor table, for example `static readonly Dictionary<PublicAddressKind, Registry>` holding the entity set, owner projection, visibility predicate and path segment. Alternatively, derive `kind` from `T` with a single `KindOf<T>()` and drop the separate parameter.

### IN-03: Weak or brittle test assertions

**File:** `CoffeePeek.Shops.Infrastructure.Tests/Persistence/CityZonePublicAddressServiceTests.cs:264-270`, `CityZonePublicAddressServiceTests.cs:139`, `CoffeePeek.Shops.Infrastructure.Tests/Persistence/PublicAddressPersistenceTests.cs:52-54`, `CoffeePeek.ShopsService.Tests/PublicAddressApiContractTests.cs:71-78,106-108`
**Issue:**
- `Disabled_NoAllocationAndServiceReturns503` lines 264-270 cannot fail: the city never had address rows, so "count is 0 after delete" is true whether or not retirement runs.
- The Draft pre-initialized zone is asserted hidden only through `ResolveAsync` (line 139), never through `GetByIdsAsync` / by-id.
- The `check.Sql.Should().Contain(value.ToString())` loop is redundant after the exact `Be(...)`, and weak on its own ("1" matches "10").
- The admin-policy test checks only the class attribute, not that no action carries `[AllowAnonymous]`.
- `AdminKind` reflects on the private method name `"Kind"`, so renaming it gives a NullReferenceException instead of a clear failure.

**Fix:** Drop or replace lines 264-270. Add `(await service.GetByIdsAsync(PublicAddressKind.CoffeeZone, [zone.Id], ct)).Should().BeEmpty()` to the Draft test. Assert `typeof(AdminPublicAddressesController).GetMethods().SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>()).Should().BeEmpty()`.

### IN-04: City retirement audit is unattributed, and a deleted city's history is unreachable

**File:** `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressWriter.cs:103`, `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs:120`
**Issue:** `retire` audit rows are written with `ActorId = null`, so an admin city deletion is not attributable. `HistoryAsync` also returns 404 once the owner row is gone, so the audit trail of a deleted city, including its retirement, cannot be read through the admin API. Cities are the first catalog kind that admins routinely delete.
**Fix:** Pass the acting user through the delete path (for example via a scoped actor accessor that the hook reads). Let `HistoryAsync` fall back to audit existence when the owner row is missing.

### IN-05: Public zone payload has no CityId, and the new endpoints are untyped in OpenAPI

**File:** `CoffeePeek.ShopsService/Controllers/PublicCoffeeZoneAddressesController.cs:21,29-31`, `CoffeePeek.ShopsService/Controllers/PublicCityAddressesController.cs:20`
**Issue:** `MapCoffeeZoneDto` has no `CityId`, so a zone page cannot link to its `/cities/{slug}` page or build breadcrumbs without another call. Separately, the by-slug actions return `IActionResult` without `[ProducesResponseType]`, so Scalar documents no response schema for the frontend handoff.
**Fix:** Plan a versioned zone DTO that includes `CityId`, since the Contract constraint prevents changing the existing one. Add `[ProducesResponseType(typeof(PublicAddressEnvelope<MapCoffeeZoneDto>), 200)]` / `[ProducesResponseType(404)]`, and the city equivalent.

---

_Reviewed: 2026-09-28T09:02:34Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_

## Fixes applied (2026-09-28)

- WR-01: fixed in `94889404` (edit names now generate distinct slugs; zone edit covered).
- WR-02: fixed in `94889404` (runbook: migrations are a deployment prerequisite regardless of the flag; `update.sh` needs `apply-migrations.sh` first).
- WR-03, WR-04: accepted (pre-existing 409 behavior; documented IMessageBus deviation).
