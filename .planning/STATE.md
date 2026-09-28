---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: in_progress
last_updated: "2026-09-28T13:59:00.000Z"
progress:
  total_phases: 4
  completed_phases: 3
  total_plans: 7
  completed_plans: 5
  percent: 71
---

# State

## Active work: public slugs (2026-09-27)

- Approved full staged rollout: `specs/PUBLIC-SLUGS.md`.
- Phase 1 implemented: commits `7aba62c2`, `0e562a2a`; Shops domain 173/173 and infrastructure 49/49 pass.
- Infrastructure verification debt: live PostgreSQL uniqueness/FK/concurrency tests pending; Docker startup requires sudo password. Model/SQL verification is not live DB verification.
- Phase 2 implementation complete: coffee shop/roaster writer coordination, slug APIs, explicit rename, bounded backfill, audit migration and compatibility tests.
- Phase 2 verification: 461 affected tests and the CI solution build pass. Live PostgreSQL uniqueness/FK/advisory-lock/savepoint checks remain required, so `PublicAddresses__Enabled` stays false by default.
- Frontend is outside this repository; page redirects remain a later external integration step.
- Phase 3 plan 01 complete (2026-09-28, commits `33f94a94`, `f04e325f`, `8233c3f1`, `3566bf48`): City and CoffeeZone typed registries, generated migration `20260928082712_AddCityAndZonePublicAddresses` (two tables, RESTRICT FKs, `CK_PublicAddressAudit_Kind` widened to `IN (1, 2, 3, 4)`), SaveChanges hook (city create/delete, zone first-publication/delete) and explicit four-kind dispatch in `PublicAddressService`/`PublicAddressWriter.Create<T>`. Tests: Shops infrastructure 64, domain 189, application 224 and ShopsService 11 all pass; `has-pending-model-changes` is clean. No live PostgreSQL verification.
- Phase 3 decisions: D-01 global per-type zone namespace (`/cities/{slug}`, `/coffee-zones/{slug}`); D-02 every city is public, zones only while Published; zones allocate on first publication, and Archive/MoveToDraft hide without retiring; city/zone delete always retires, even when disabled.
- Phase 3 plan 02 complete (2026-09-28, commits `ca38eb22`, `ee185d94`, `e370ad3b`):
  - Public `api/Catalogs/cities` and `api/Catalogs/coffee-zones` address endpoints (by-slug, batch, legacy GUID; NoStore). Zone by-slug re-checks `Status == Published` on the data read. City data is read AsNoTracking via the new `IQueryCityRepository.GetById`.
  - Admin address routes accept `cities|coffee-zones` with an explicit `Kind()` switch (400 on unknown, Admin policy kept).
  - `CoffeePeek.ShopsService.Tests` added to `CoffeePeek.Backend.ci.slnf` (D-05).
  - `docs/api/public-addresses.md` covers the city/zone contract, catalog filter values (D-04) and the extended live gate.
  - Tests: ShopsService 25 (Debug and Release), Shops domain 189, application 224 and infrastructure 64 all pass. The CI slnf Release build has 0 errors and `has-pending-model-changes` is clean. No live PostgreSQL verification.
- Phase 3 plan 02 decisions:
  - Public city/zone controllers inject `IPublicAddressService` and read-only queries directly, not `IMessageBus`: no new handlers and no Wolverine codegen.
  - No slug search params; the frontend resolves readable values to the unchanged GUID filters.
- Phase 3 verification: passed (`03-VERIFICATION.md`). The live PostgreSQL gate (phases 1–3) is still open, so `PublicAddresses__Enabled` stays false.
- Phase 4 plan 01 complete (2026-09-28, commits `5adff179`, `7c92a725`, `2f43ea54`, `824457f1`):
  - Account-local `UserPublicAddresses` registry (Shops-identical schema, RESTRICT FK to Users, no navigation), generated migration `20260928134755_AddUserPublicAddresses` (one table, three indexes, four checks). This migration is a deployment prerequisite whatever the flag says, because deletion always retires.
  - `AccountDbContext` SaveChanges hook through `ProfileAddressWriter`/`ProfileAddressTransaction` (advisory lock `7402100401`): soft or hard delete always retires every row; while enabled, creation allocates (email and Google) and a username change follows under Policy A (alias kept, A→B→A restores, same-slug change is a no-op).
  - `PublicAddressOptions` bound from `PublicAddresses`; unset, so default false.
  - Tests: Account infrastructure 72 (baseline 54), domain 255 (baseline 231), application 214. The Wolverine codegen guard is green, `has-pending-model-changes` is clean, and the CI slnf Release build has 0 errors. No live PostgreSQL verification.
- Phase 4 plan 01 decisions:
  - Change detection compares `OriginalValue` with the current value, never `IsModified`, because `UserRepository.Update` marks every property modified. The writer saves only via `SaveCoreAsync`, so the hook never re-enters.
  - The candidate search is owner-aware: the owner's own canonical is a no-op and the owner's own alias is restored. Another user's alias or a tombstone forces that owner's `-<8hex>` suffix.
  - Reserved, unsupported-script and bare `user` usernames always get `user-<8hex>`, never a bare `place` or `user`.
  - Retirement runs before the enabled check. Block/unblock never touches addresses. A soft-deleted user's username rewrite allocates nothing.
  - A user created while disabled gets an address on the first enabled username change.
- Next: Phase 4 plan 02 (ProfileAddressService, public `api/Users` endpoints, admin backfill, GUID-route 404 fix).

## Quick Tasks Completed

| ID | Slug | Status | Date |
|----|------|--------|------|
| 260801-erp | remove-community-social-layer-feed-posts | complete | 2026-08-01 |
| 260815-fast | widen-import-candidate-phone | complete | 2026-08-15 |
| 260815-fast | admin-overview-always-returns | complete | 2026-08-15 |
| 260831-krk | add-full-crud-create-update-delete-for-c | complete | 2026-08-31 |
| 260901-e0n | fix-400-validation-failed-on-post-api-mo | complete | 2026-09-01 |
| 260901-el2 | fix-checkins-createdat-returning-default | complete | 2026-09-01 |
| 260901-faa | fix-checkin-photos-on-create-persist-pri | complete | 2026-09-01 |
| 260901-m4f | fix-sentry-cp-shops-service-34-conflicte | complete | 2026-09-01 |
| 260909 | roaster-contribution | complete | 2026-09-09 |
| 260914 | coffee-shop-data-completeness | complete | 2026-09-14 |
| 260914-zones | coffee-zones-and-map-clustering | complete | 2026-09-14 |
| 260917-f9m | link-published-shops-reviews-to-source-m | complete | 2026-09-17 |
| 260921 | account-deletion-workflow-slice | complete | 2026-09-21 |
| 260922 | shop-change-requests | complete | 2026-09-22 |
| 260924-hek | account-security-group1-fixes | complete | 2026-09-24 |
| 260924-jpd | harden-review-and-check-in-photo-attachm | complete | 2026-09-24 |
| 260924-mii | copy-moderation-review-photos-to-publish | complete | 2026-09-24 |
| 260924-oxa | load-moderation-review-photos-on-approve | complete | 2026-09-24 |
| 260925-ei9 | admin-stats-expansion-dau-wau-mau-timese | complete | 2026-09-25 |
| 260925-kjj | add-optional-date-range-filter-from-to-t | complete | 2026-09-25 |
| 260925-l0p | imgproxy-image-variants-with-urls-on-pho | complete | 2026-09-25 |

## Milestone Progress

**Tier 0** waves **A–E** are done (security, account/auth, admin sessions, password reset, check-ins/favorites cleanup, related tests).

## Specs / next

- Shop filters & tags SPEC written at `.planning/specs/SHOP-FILTERS-TAGS.md`.
- Catalog ingest + admin queue SPEC: `.planning/specs/SHOP-IMPORT-ADMIN.md` (backend vs frontend handoff).
- Coffee shop menu (catalog drinks + Gemini photo parse): `.planning/specs/SHOP-MENU.md`.
- Roaster contribution (user submission -> moderation -> publish, public detail endpoint) SPEC + implementation: `.planning/specs/ROASTER-CONTRIBUTION.md`, `.planning/quick/260909-roaster-contribution/`.

## Accumulated Context

### Exploration (2026-08-15)

Specialty coffee shop bootstrap for Minsk: OSM + lists → admin review queue → catalog. See note `2026-08-15-specialty-coffee-shops-minsk-import`, seed `SEED-001`.

Spike 001 VALIDATED: Overpass Minsk = 1576 candidates (priority ~380, vending 96). OSM is not a specialty catalog. Draft aggregate: `ShopImportCandidate`, not `ModerationShop`. Import labels **ShopKind** (Specialty / GoodCoffee / Cafe / ToGo); reject stays out of the feed. Review card must show Instagram + Yandex photos/images + website preview — coordinates alone are not enough. OSM objects not edited in 5+ years are dropped from the queue.

### Account deletion (2026-09-21)

First slice complete: pending request persistence, create/resend/current/cancel APIs, HMAC-protected confirmation tokens, and immediate confirmation email. Users and sessions remain active; confirmation and cross-service cleanup are deferred.

### Pending Todos

_None._
