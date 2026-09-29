---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: in_progress
last_updated: "2026-09-29T13:35:00.000Z"
progress:
  total_phases: 5
  completed_phases: 4
  total_plans: 15
  completed_plans: 9
  percent: 60
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
- Phase 4 plan 02 complete (2026-09-28/29, commits `94a7fdc4`, `359a7900`, `9705063a`, `bd2b746d`; handler RED facts landed in user docs commit `21419ba3`):
  - `IProfileAddressService`/`ProfileAddressService`: live visibility `!IsSoftDelete && !IsBlocked` on resolve and batch (email confirmation not required), `/users/{slug}` canonical paths, bounded backfill (dry-run, id cursor, replay reports alreadyInitialized, deleted excluded, blocked included, unique violation -> 409), 503 while disabled.
  - Anonymous NoStore `GET api/Users/by-slug/{slug}` (envelope via existing `GetPublicUserProfileCommand` for the resolved owner), `GET api/Users/public-addresses?ids=`, `GET api/Users/{id:guid}/public-address`; every hidden case gives the identical 404 with the generic ProblemDetails body (no user data).
  - Admin-only `POST api/admin/users/public-addresses/backfill` is the only admin address action for users (D-06, D-10).
  - D-04: `GET api/Users/{id:guid}` now returns 404 for soft-deleted and blocked users (body-only handler change; pre-generated Wolverine handler unchanged). Shops `AccountUserExistenceLookup` 404 now means "not exists or not visible"; no Shops change.
  - Tests: Account application 216 (baseline 214), infrastructure 89 (72 + 7 service + 10 controller, Wolverine codegen guard green), domain 255. CI slnf Release build 0 errors. No live PostgreSQL/HTTP verification.
- Phase 4 plan 02 decisions:
  - Public address controllers read metadata from the read-only `IProfileAddressService` (no new Wolverine handler, no codegen drift) but read profile data through `IMessageBus` with the existing command (one mapping path, D-04 re-check); a `NotFoundException` from the read becomes `NotFound()`.
- Phase 4 plan 03 complete (2026-09-29, commits `ec26f930`, `05de23df`, `85f5b428`):
  - Gateway `account-Users-by-slug-route` (`/api/Users/by-slug/{slug}` -> account-cluster, `auth-endpoints`, no AuthorizationPolicy) before the Users catch-all. `RoutePrecedence.ComputeInbound` test: the `reviews` slug matches all three templates and by-slug wins; real Shops `/api/users/{guid}/reviews` paths do not match it (D-07).
  - `docs/api/public-addresses.md`: user profile contract (`/users/{slug}`, alias example), username policy, visibility (identical 404 with generic ProblemDetails body, no user data), D-04 GUID fix and Shops `AccountUserExistenceLookup` now meaning "exists and visible", deletion/tombstones, `noindex` note, backfill-only admin API, `20260928134755_AddUserPublicAddresses` as a flag-independent deployment prerequisite, per-service flag, pending Account live PostgreSQL checks plus HTTP smoke run, D-02 squatting decision as an enablement step.
  - Phase gate: CI slnf Release build 0 errors; Account domain 255, application 216, infrastructure 89, Gateway 56 (all pass); `has-pending-model-changes` clean; isolation diff vs `96acf741` clean; flag unset in JSON/env/AppHost. No live PostgreSQL/HTTP/YARP verification.
- Phase 4 plan 03 decisions:
  - The reviews-slug capture is fixed by the dedicated gateway route, not by reserving `reviews` in the shared `PublicSlug` list.
  - The flag is enabled per service; AccountService needs its own live gate and the D-02 decision first.
- Next: Phase 4 verification. The live PostgreSQL gate (phases 1–4) is still open, so `PublicAddresses__Enabled` stays false for ShopsService and AccountService.
- Phase 5 plan 01 complete (2026-09-29, commits `d356798e`, `47fc3f69`, `84a96b01`):
  - Opt-in live PostgreSQL harness: the single shared helper `CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresServer.cs` (namespace `CoffeePeek.Testing`), linked into `CoffeePeek.Account.Infrastructure.Tests` through `<Compile Include ... Link=...>` (D-01). No new project or package.
  - Contents:
    - guards: owned-name regex `\A...\z` with the 63-byte limit, only `Database=postgres`, no Password key, superuser only with `COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER=1`, 6 h sweep, registry allowlist;
    - create/drop/sweep: `DROP ... WITH (FORCE)`, with the name guard before any I/O;
    - pg_locks and pg_stat_activity probes;
    - raw registry-row and uncommitted-tombstone helpers;
    - the `LiveDatabase<TContext>` fixture base.
  - Connect errors are sanitized: no host in any message.
  - Tests with the env var unset:
    - guard class 128/128 (runs in CI);
    - LivePostgres trait `Total: 4, Skipped: 4`;
    - Shops infrastructure 196 (192 passed = 64 + 128, 4 skipped);
    - Account infrastructure 89.
  - Nothing ran live.
- Phase 5 plan 01 decisions:
  - Research A1 verified: `Assert.SkipUnless` in the test-class constructor is reported as Skipped, by both the in-process runner and `dotnet test` (VSTest). A2 verified: exit 0 when all filtered tests are skipped. Later plans keep the constructor skip.
  - `LivePostgresServer.OpenAsync` is public, an addition to the contract. Later plans open connections through it so connect failures never print the host.
  - Probes and raw writes refuse non-`cp_live_*` databases before connecting. The sweep is best effort per database.
  - On this machine's Git Bash grep 3.0, `\\` does not match a literal backslash. Use `grep -F` for backslash paths in acceptance checks.
- Phase 5 plan 02 complete (2026-09-29, commits `6386fce5`, `25b6e4ec`, `6073c62d`):
  - D-05: Shops `PublicAddressService.BackfillAsync` is async and maps `DbUpdateException` when `IsAddressConflict` to `PublicAddressException` 409 "Address is reserved or changed concurrently." (the same message and status as `ChangeAsync`, parity with Account). Unrelated DB errors still propagate. No interface, contract, controller or migration change.
  - Tests: `PublicAddressBackfillConflictTests` 8/8, red before the fix (5 failing 409 rows). They use a throwing `SaveChangesInterceptor` on InMemory. Shops infrastructure 204 (200 passed = 192 + 8, 4 live skipped); Shops application 224; ShopsService 25; Account infrastructure 89.
  - `docs/api/public-addresses.md`:
    - `## Live PostgreSQL gate`: coverage table by test class, server/role, credentials (D-02), guards (D-03), run commands, pass rule (D-04), cleanup, not covered (D-09, D-10), `### HTTP smoke (manual)` (D-06), `### Live gate evidence` ("No green run has been recorded yet.");
    - `## Dry-run backfill review`: page-by-page loop with a y/N pause, same-cursor write, diff and stop-on-error, plus the replay check and the 409 retry (D-07);
    - `### Rollback rehearsal`.
  - Nothing ran live. The flag stays unset everywhere.
- Phase 5 plan 02 decisions:
  - Shops backfill 409 reuses `IsAddressConflict` and the exact `ChangeAsync` message; only address constraints are mapped.
  - The runbook loop asks for confirmation before each write and stops on any HTTP error, 409 or diff; `CURSOR` stays on the stopped page.
- Next: Phase 5 remaining plans (05-03..05-08). The live gate stays open until 05-08 (attested) or 05-07 reports `Failed: 0, Skipped: 0`.

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
