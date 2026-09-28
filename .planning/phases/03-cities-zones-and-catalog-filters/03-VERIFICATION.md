---
phase: 03-cities-zones-and-catalog-filters
verified: 2026-09-28T09:09:36Z
status: passed
score: 14/14 must-haves verified
overrides_applied: 0
infrastructure_gap: # Documented enablement gate (same precedent as phases 1-2: passed-with-infrastructure-gap). Not a phase gap: feature is default-off and the gate is documented in docs/api/public-addresses.md "Enablement gate" step 2.
  reason: "Live PostgreSQL unavailable (Docker daemon pipe missing); InMemory and Npgsql design-time model checks only"
  open_items:
    - "Unique Slug and filtered canonical owner indexes on CityPublicAddresses / CoffeeZonePublicAddresses under concurrent allocation (advisory-lock serialization of two concurrent 'Минск' creations)"
    - "FK_CityPublicAddresses_Cities_OwnerId / FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId RESTRICT enforcement"
    - "City delete while zones exist returns 409 and the address retirement rolls back (savepoint path)"
    - "CK_PublicAddressAudit_Kind accepts 3 and 4; CK_*_Lifecycle rejects canonical row without owner"
    - "Phase 1/2 shop/roaster live items (uniqueness, FKs, concurrent rename, savepoint rollback) remain open"
  default_off_confirmed: "PublicAddressOptions.Enabled defaults false; bound from section 'PublicAddresses' only; set in no appsettings, deploy file or AppHost"
advisories: # Non-blocking; from 03-REVIEW.md, re-assessed against the codebase
  - id: WR-01
    severity: warning
    summary: "City display-name edit test is vacuous ('Минск' and 'Minsk' both slug to 'minsk'); no Published-zone edit test"
    goal_impact: "none - behavior holds structurally: neither the ShopsDbContext gate nor PrepareOwnerChangesAsync has any Modified-City branch, and CoffeeZone.Update never touches Status. Regression-lock strength only (project core value)."
  - id: WR-02
    severity: warning
    summary: "City delete always queries CityPublicAddresses (even when disabled); runbook lists the migration only under the enablement gate and says 'existing GUID APIs are unchanged' when disabled"
    goal_impact: "none on the default deploy path - deploy/scripts/ci-deploy.sh and deploy.sh apply all migrations before restarting containers unless SKIP_MIGRATIONS=1. Same pattern already accepted for shop/roaster deletes in phase 2. Runbook wording should say the three migrations are a deployment prerequisite independent of the flag."
  - id: WR-03
    severity: warning
    summary: "City-with-zones delete relies on the FK RESTRICT DbUpdateException -> generic 409; unguarded and untested"
    goal_impact: "none - pre-existing behavior of DELETE api/admin/cities/{id}; PublicAddressTransaction.RunAsync rethrows the original exception so UnitOfWork still maps it to 409; rollback of the retirement is a documented live-gate item"
  - id: WR-04
    severity: warning
    summary: "Public city/zone controllers inject IQueryCityRepository/ICoffeeZoneQueries instead of IMessageBus and duplicate the /api/Map zone mapping"
    goal_impact: "none - deliberate, documented deviation (avoids Wolverine codegen); field mapping identical to CoffeeShopQueries.GetMap; ShopCount comes from a separate counter (CoffeeZoneQueries.CountMembers) that can diverge from the map counter in override edge cases"
---

# Phase 3: Cities, zones and catalog filters Verification Report

**Phase Goal:** extend readable stable public addresses to cities, published coffee zones and the public catalog filter values that appear in page/filter URLs, reusing the phase 1–2 slug rules, registries/coordinator, visibility-aware lookup, rename/history and bounded backfill. Existing GUID routes and DTOs stay unchanged; feature stays default-off behind the same live PostgreSQL gate. Account profiles (phase 4) and the external frontend (phase 6) are out of scope.
**Verified:** 2026-09-28T09:09:36Z
**Status:** passed (with the documented live-PostgreSQL infrastructure gap, per the phase 1–2 precedent)
**Re-verification:** No, initial verification (no prior 03-VERIFICATION.md)

The roadmap has no `success_criteria` for this phase, so the must-haves are the 14 plan frontmatter truths (03-01: 8, 03-02: 6). The coverage set is research labels P3-01..P3-06 plus CONTEXT decisions D-01..D-05. Every one is mapped below.

## Goal Achievement

### Observable Truths

| # | Truth (source) | Status | Evidence |
|---|---|---|---|
| 1 | D-01: separate global per-type registries; same slug once per kind; `/cities/{slug}`, `/coffee-zones/{slug}`; same-name zones in two cities get distinct slugs (03-01) | VERIFIED | `CityPublicAddressConfiguration`/`CoffeeZonePublicAddressConfiguration` reuse unchanged generic `Configure<T>` (unfiltered unique `IX_{table}_Slug`, no CityId). `PathSegment` switch returns `cities`/`coffee-zones` (PublicAddressService.cs:220-227). `Namespaces_ArePerKindAndZoneNamespaceIsGlobal` passes (`minsk` in shop, city and zone; `nemiga` plus `nemiga-…`). |
| 2 | D-02: every City resolves and appears in batch; a CoffeeZone does only while Published; Draft/Archived return null like unknown slugs (03-01) | VERIFIED | `IsVisibleAsync` City `AnyAsync(Id)`, CoffeeZone `Id && Status == Published` (lines 195-204); `VisibleIdsAsync` same filters (206-214). Zone publish/archive/draft tests assert null resolve and an empty batch. |
| 3 | D-03: only City and CoffeeZone gain registries (03-01) | VERIFIED | `git ls-files` for `(ShopTag|BrewMethod|CoffeeBean|Equipment)PublicAddress` gives 0. `ShopTagAggregate` is unchanged since d812e0c0. The migration touches only the 2 new tables plus the audit check. |
| 4 | P3-01: city create allocates one canonical from Name; `Update(name)` never changes it; delete tombstones all rows even when disabled (03-01) | VERIFIED | Writer: Added City leads to `InitializeAsync` (lines 37-39); Deleted City leads to `RetireAsync` before the `if (!PublicAddressesEnabled) return` guard (24-25, 29). There is no Modified-City branch in the writer or in the `ownerChanges` gate (ShopsDbContext.cs:37-40). Tests `City_CreateRenameRestoreDelete_KeepsStableAddress` and `CityDelete_RetiresAddressesEvenWhenDisabled` pass. The display-edit assertion is vacuous (WR-01), but the code path structurally cannot rename. |
| 5 | P3-02: zone gets an address only on first publication; Archive/MoveToDraft hide without retiring; republish reuses the row and revision; an admin-initialized Draft keeps its slug (03-01) | VERIFIED | `IsNewlyPublished` covers Added+Published, or Modified with `Status` IsModified (writer 47-50). The production path `SetCoffeeZoneStatusHandler` loads the zone tracked (`CoffeeZoneRepository.GetByIdAsync`, no AsNoTracking), calls `Publish()` and then `UnitOfWork.SaveChangesAsync`, which reaches the overridden `SaveChangesAsync(bool, ct)`. `CoffeeZone.Update` never touches Status. `InitializeAsync` is idempotent (returns the existing canonical). Tests `Zone_PublishArchiveRepublish_HidesWithoutRetiring` and `DraftZone_AdminInitializeIsKeptOnPublish` pass. |
| 6 | P3-04: initialize, A->B->A rename, history, dry-run/cursor/replay backfill for City (all) and Zone (Published only); audit Kind 3/4 (03-01) | VERIFIED | 4-arm switches in Change/History/Backfill. `ListOwnersAsync` City has no filter, Zone is `Status == Published` and projected (never materialized). `GetStemAsync` has no zone status filter (admin pre-init). Tests `Backfill_CitiesAllZonesPublishedOnly_DryRunCursorReplay` and the history/audit Kind assertions pass. |
| 7 | P3-06: shop/roaster behavior unchanged (PublicAddressServiceTests.cs byte-identical); flag defaults false; no Wolverine handler changes (03-01) | VERIFIED | `git diff --quiet d812e0c0` on `PublicAddressServiceTests.cs`, `PublicAddressTransaction.cs`, `Application/Features`, `ShopsService/Internal/Generated` exits 0. Shop/roaster arms keep the prior queries. `PublicAddressOptions.Enabled` defaults false. `WolverinePreGeneratedHandlersTests` pass. |
| 8 | `CK_PublicAddressAudit_Kind` widened to `IN (1, 2, 3, 4)` in the generated migration (03-01) | VERIFIED | `20260928082712_AddCityAndZonePublicAddresses.cs` Up: DropCheckConstraint (14-16) plus AddCheckConstraint `"Kind" IN (1, 2, 3, 4)` (72-75). `Model_AuditKindCheckCoversEveryAddressKind` passes. Forbidden ops (Alter/Add/Drop/Rename column, DropIndex, RenameIndex) count is 0. |
| 9 | P3-03/D-01: `api/Catalogs/cities/by-slug/{slug}` returns `PublicAddressEnvelope<CityDto>`; `api/Catalogs/coffee-zones/by-slug/{slug}` returns `PublicAddressEnvelope<MapCoffeeZoneDto>`; both have `public-addresses?ids=` and `{id:guid}/public-address` (03-02) | VERIFIED | `PublicCityAddressesController.cs`, `PublicCoffeeZoneAddressesController.cs` with the exact routes. Reflection route/template tests pass. No collision with `CatalogsController` (`GET api/Catalogs/cities`, single segment). The gateway `shops-Catalogs-route` `/api/Catalogs/{**remainder}` (anonymous) already forwards. |
| 10 | D-02: zone by-slug re-checks Published on the data read and returns 404 for Draft/Archived; every city readable (03-02) | VERIFIED | `if (zone is null || zone.Status != CoffeeZoneStatus.Published) return NotFound();`. Controller tests cover the Draft/Archived theory, a missing zone, and a city deleted between resolve and read. |
| 11 | P3-04: admin routes accept `cities`/`coffee-zones`, map explicitly, reject others with 400, still Admin policy (03-02) | VERIFIED | Route regex `^(shops|roasters|cities|coffee-zones)$`, `Kind()` switch with `_ => throw PublicAddressException(400)`, and `[Authorize(Policy = RoleConsts.Admin)]` unchanged. `AdminRouteEntities_CoverEveryAddressKind` ties the regex set to `Enum.GetValues<PublicAddressKind>()`. |
| 12 | D-03/D-04/P3-05: no slug filter params; `GET api/CoffeeShops`, `GET api/Map`, `GET api/Catalogs/*` and GUID routes/DTOs unchanged; docs list the stable filter values (03-02) | VERIFIED | `git diff --quiet d812e0c0` exits 0 on `CoffeePeek.Contract`, `CoffeePeek.Gateway`, `CoffeeShopsController`, `MapController`, `CatalogsController`, `PublicCoffeeShopAddressesController`, `PublicRoasterAddressesController`. The search filters are GUID/enum (CoffeeShopsController.cs:34-47). The docs section `## Catalog filter values` matches the code: `ShopTagDto(Id, Slug, …)` is returned by `GET api/Catalogs/shop-tags`. |
| 13 | D-05: `CoffeePeek.ShopsService.Tests` in `CoffeePeek.Backend.ci.slnf` (03-02) | VERIFIED | The slnf lists it. `.github/workflows/ci-cd.yml:70` runs `dotnet test "CoffeePeek.Backend.ci.slnf"`. The Wolverine host test is designed to pass without live infrastructure (points at port 1), so it is safe for CI. |
| 14 | P3-06: flag default false and configured nowhere; docs extend the live gate and claim no live verification (03-02) | VERIFIED | `grep -rnE 'PublicAddresses(__|:|")'` over appsettings, deploy/, AppHost and Gateway returns nothing. The only binding is `Configure<PublicAddressOptions>(GetSection("PublicAddresses"))`. Docs gate step 1 lists all three migrations; step 2 lists the city/zone checks as "still pending … none of them has been verified". |

**Score:** 14/14 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CityPublicAddress.cs` | Typed city registry row | VERIFIED | `public sealed class CityPublicAddress : PublicAddress`, EF ctor, `Create` factory; used by writer/service/config |
| `CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CoffeeZonePublicAddress.cs` | Typed zone registry row | VERIFIED | Same shape; wired |
| `CoffeePeek.Shops.Persistance/Configuration/PublicAddressAuditConfiguration.cs` | Widened check | VERIFIED | `"Kind" IN (1, 2, 3, 4)` |
| `CoffeePeek.Shops.Persistance/Migrations/20260928082712_AddCityAndZonePublicAddresses.cs` (+ Designer, snapshot) | 2 tables, 6 indexes, RESTRICT FKs, check widening | VERIFIED | Exact shape; `has-pending-model-changes` reports "No changes have been made to the model since the last migration." (re-run by the verifier) |
| `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs` | Four-kind dispatch everywhere | VERIFIED | `PublicAddressKind.City =>` 9, `PublicAddressKind.CoffeeZone =>` 9, `kind == PublicAddressKind` 0; `IsAddressConflict` has both new prefixes |
| `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressWriter.cs` | Lifecycle branches, explicit `Create<T>` | VERIFIED | `IsNewlyPublished`; `Create<T>` 4-arm type switch plus `InvalidOperationException` |
| `CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs` | DbSets, configs, gate | VERIFIED | 2 DbSets, 2 `ApplyConfiguration`, gate uses `IsNewlyPublished` |
| `CoffeePeek.Shops.Infrastructure.Tests/Persistence/CityZonePublicAddressServiceTests.cs` | ≥150 lines of lifecycle tests | VERIFIED | 339 lines, 8 tests, all pass |
| `CoffeePeek.ShopsService/Controllers/PublicCityAddressesController.cs` | City endpoints | VERIFIED | `[Route("api/Catalogs/cities")]`, NoStore |
| `CoffeePeek.ShopsService/Controllers/PublicCoffeeZoneAddressesController.cs` | Published-only zone endpoints | VERIFIED | `[Route("api/Catalogs/coffee-zones")]`, NoStore, Published re-check |
| `CoffeePeek.ShopsService/Controllers/AdminPublicAddressesController.cs` | Four admin kinds | VERIFIED | Regex and explicit switch |
| `CoffeePeek.Shops.Domain/.../IQueryCityRepository.cs` + `QueryCityRepository.cs` | `GetById` AsNoTracking | VERIFIED | `_repository.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)` |
| `CoffeePeek.ShopsService.Tests/PublicCatalogAddressControllerTests.cs` | ≥60 lines of controller behavior tests | VERIFIED | 182 lines, 8 cases, hand-written fakes, no Moq |
| `docs/api/public-addresses.md` | City/zone contract, filter values, gate | VERIFIED | Contains `## Cities and coffee zones`, `## Catalog filter values`, all three migration ids, FK names, `CK_PublicAddressAudit_Kind`, `laptop_friendly`, pending-gate wording |
| `CoffeePeek.Backend.ci.slnf` | Includes ShopsService.Tests | VERIFIED | Valid JSON; Release build 0 errors (orchestrator) |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `ShopsDbContext.SaveChangesAsync` | `PublicAddressWriter.PrepareOwnerChangesAsync` | `ownerChanges` gate with City/CoffeeZone, `IsNewlyPublished` | WIRED | Precedence `is A or B … && …` parses as intended; wrapped in the unchanged `PublicAddressTransaction.RunAsync` |
| Admin write handlers (`CreateCityHandler`, `DeleteCityHandler`, `SetCoffeeZoneStatusHandler`) | SaveChanges hook | `IUnitOfWork.SaveChangesAsync` then `context.SaveChangesAsync(ct)` | WIRED | Tracked loads; no `ExecuteUpdate`/bulk/raw-SQL writes to Cities or CoffeeZones anywhere in Shops; `new City(` exists only in `CreateCityHandler` |
| `PublicAddressService` | `db.CoffeeZones` | `CoffeeZoneStatus.Published` filter in IsVisible/VisibleIds/ListOwners | WIRED | Projected queries only |
| `PublicAddressConfiguration` | `Cities` / `CoffeeZones` | `HasOne<City>()` / `HasOne<CoffeeZone>()` Restrict | WIRED | Model test asserts the principal CLR type and Restrict |
| `PublicCoffeeZoneAddressesController` | `IPublicAddressService.ResolveAsync(CoffeeZone)`, then `ICoffeeZoneQueries.GetByIdAsync` | Resolve, then Published re-check | WIRED | `LastKind` assertion in tests |
| `PublicCityAddressesController` | `IQueryCityRepository.GetById` | Resolve, then AsNoTracking read | WIRED | Not the `city:list:all` cache |
| `AdminPublicAddressesController.Kind` | `PublicAddressKind` | `"coffee-zones" => PublicAddressKind.CoffeeZone` | WIRED | Reflection coverage test |
| Gateway | New endpoints | `shops-Catalogs-route` (anonymous), admin cities/coffee-zones routes (Moderator; service enforces Admin) | WIRED | Gateway `UseResponseCaching` honors `NoStore` |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `PublicCityAddressesController.GetBySlug` | `address`, `city` | `PublicAddressService.ResolveAsync`, a registry query plus a visibility query; `QueryCityRepository.GetById` DB read | Yes | FLOWING |
| `PublicCoffeeZoneAddressesController.GetBySlug` | `address`, `zone` | Registry and visibility, then `CoffeeZoneQueries.GetByIdAsync` (DB zone plus active non-hidden shops plus overrides) | Yes (ShopCount from `CountMembers`, not the map counter; see WR-04) | FLOWING |
| Batch/by-id endpoints | `PublicAddressMetadata[]` | `GetByIdsAsync` with `VisibleIdsAsync` DB filter | Yes | FLOWING |

### Behavioral Spot-Checks (run by the verifier)

| Behavior | Command | Result | Status |
|---|---|---|---|
| City/zone lifecycle, namespaces, backfill, disabled mode, model shape, shop/roaster regressions | `dotnet test CoffeePeek.Shops.Infrastructure.Tests --no-build --filter FullyQualifiedName~PublicAddress` | 17 passed, 0 failed | PASS |
| Routes, NoStore, admin kind mapping, zone re-check, DTO mapping, Wolverine codegen | `dotnet test CoffeePeek.ShopsService.Tests --no-build` | 25 passed, 0 failed | PASS |
| Slug regression rows and 4-kind domain lifecycle | `dotnet test CoffeePeek.Shops.Domain.Tests --no-build --filter FullyQualifiedName~PublicAddresses` | 44 passed, 0 failed | PASS |
| No model drift | `dotnet ef migrations has-pending-model-changes` (dummy connection string) | "No changes have been made to the model since the last migration." | PASS |
| Whitespace/diff hygiene | `git diff --check d812e0c0 HEAD` | clean | PASS |

Orchestrator evidence (not re-run): `dotnet build CoffeePeek.Backend.ci.slnf -c Debug` 0 errors; full suites Domain 189/189, Application 224/224, Infrastructure 64/64, ShopsService 25/25 (total 502, up from phase 2's 461); `verify.schema-drift` found none.

### Probe Execution

Step 7c: SKIPPED. No `scripts/*/tests/probe-*.sh` exist, and no phase PLAN or SUMMARY declares a probe.

### Requirements Coverage (P3 labels and CONTEXT decisions; no REQUIREMENTS.md)

| ID | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| P3-01 | 03-01 | City address allocated on create, retired on delete, never renamed by a display edit | SATISFIED | Truth 4 |
| P3-02 | 03-01 | Published zone address; hidden when Draft/Archived; not retired by archive | SATISFIED | Truth 5 |
| P3-03 | 03-01, 03-02 | Visibility-aware by-slug/batch/legacy-GUID lookup; alias resolves to canonical | SATISFIED | Truths 1, 2, 9, 10 |
| P3-04 | 03-01, 03-02 | Admin initialize/rename A->B->A/history/backfill for cities and zones | SATISFIED | Truths 6, 11 |
| P3-05 | 03-02 | Stable catalog filter values documented; GUID filter contracts unchanged | SATISFIED | Truth 12 |
| P3-06 | 03-01, 03-02 | GUID routes/DTOs unchanged; default-off; live gate extended | SATISFIED | Truths 7, 14 |
| D-01 | 03-01, 03-02 | Global per-type zone namespace; separate city namespace; flat paths | HONORED | Truths 1, 9 |
| D-02 | 03-01, 03-02 | All cities public; zones only while Published | HONORED | Truths 2, 10 |
| D-03 | 03-01 | Registries for City and CoffeeZone only; ShopTag keeps its slug; others deferred | HONORED | Truth 3 |
| D-04 | 03-02 | No slug filter params; docs map readable values to GUIDs | HONORED | Truth 12 |
| D-05 | 03-02 | ShopsService.Tests in CI slnf | HONORED | Truth 13 |

No orphaned labels or decisions. All 11 are accounted for.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---|---|---|---|
| (all 24 phase files) | — | TBD/FIXME/XXX/TODO/HACK/PLACEHOLDER | none found | — |
| `CityZonePublicAddressServiceTests.cs` | 38-42 | Vacuous assertion (same slug before and after the edit), WR-01 | Warning | Weak regression lock; the behavior itself is structurally guaranteed |
| `CityZonePublicAddressServiceTests.cs` | 264-270 | Cannot-fail assertion (a city with no rows deleted while disabled), IN-03 | Info | Covered separately by `CityDelete_RetiresAddressesEvenWhenDisabled` |
| `docs/api/public-addresses.md` | 7, 17 | Migration listed only as an enablement step; "existing GUID APIs are unchanged" when disabled holds only once the migrations are applied, WR-02 | Warning | Only affects `SKIP_MIGRATIONS=1`/manual deploys; the default deploy applies migrations first |
| `PublicCoffeeZoneAddressesController.cs` | 25-31 | Mapping and ShopCount duplicated from `/api/Map`, WR-04 | Warning | Possible ShopCount divergence in membership-override edge cases; shape identical |

### Review Findings: Blocking Assessment

- **WR-01: not blocking.** The truth "City.Update(name) never changes the address" holds in code. No Modified-City path exists in the gate (ShopsDbContext.cs:37-40) or in the writer. Zone edits cannot allocate because `CoffeeZone.Update` never assigns `Status`. The test is weak as a regression lock. Recommend changing it to `city.Update("Столица Беларуси")`, asserting revision 1 and one audit row, and adding a Published-zone `Update` case.
- **WR-02: not blocking.** It is real, but the same pattern was accepted for shop/roaster deletes in phase 2. `deploy/scripts/ci-deploy.sh` and `deploy.sh` apply all migrations before `update.sh`/`compose up`. Recommend a runbook fix: move the three migrations into a "Deployment prerequisite (independent of the flag)" note and qualify line 17.
- **WR-03: not blocking.** Deleting a city that still has zones already returned the generic 409 before this phase. `PublicAddressTransaction.RunAsync` rethrows the original `DbUpdateException`, so the 409 mapping is preserved. Rolling back the retirement is a documented live-gate item. A domain-specific guard in `DeleteCityHandler` would be a handler change requiring codegen, which is out of scope here.
- **WR-04: not blocking.** It is a deliberate, plan-documented deviation. The field mapping is identical to `/api/Map`. ShopCount parity is not a phase must-have.

### Human Verification Required

None for phase completion. The only manual-only item in 03-VALIDATION.md is the live PostgreSQL gate. The phase goal defines that as the enablement boundary ("feature stays default-off behind the same live PostgreSQL gate"). It is documented as pending and recorded under `infrastructure_gap` in the frontmatter, following the phase 1–2 precedent.

### Gaps Summary

No goal gaps. The codebase delivers what the phase set out to build:
- City and published CoffeeZone public addresses on the phase 1–2 coordinator, with explicit four-kind dispatch everywhere and no default-to-Roaster fallbacks left.
- A drift-free generated migration that also widens the audit kind check.
- Public by-slug, batch and legacy-GUID endpoints under the already-routed `api/Catalogs` prefix, with a Published re-check on the data read.
- Admin management for the new kinds under the Admin policy.
- Documented stable catalog filter values with no search contract change.
- ShopsService.Tests in CI.
- The feature still default-off and unconfigured.

The only open items are the live PostgreSQL gate checks (a documented enablement prerequisite) and four non-blocking review warnings, of which WR-01 (test strength) and WR-02 (runbook wording) are worth a quick follow-up before enablement.

---

_Verified: 2026-09-28T09:09:36Z_
_Verifier: Claude (gsd-verifier)_
