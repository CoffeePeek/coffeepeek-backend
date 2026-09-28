---
phase: 02-shop-roaster-public-addresses
plan: 01
subsystem: public-api
tags: [slugs, dotnet, ef-core, postgresql, compatibility]
requires:
  - 01-public-address-foundation
provides:
  - Transactional shop/roaster public-address allocation, rename, alias restore and retirement
  - Visibility-aware slug, batch and legacy-GUID lookup APIs
  - Admin history, optimistic rename and bounded resumable backfill APIs
  - External frontend redirect and guarded rollout runbook
affects: [shops-api, roasters-api, moderation-publication, import-publication, external-frontend]
tech-stack:
  added: []
  patterns: [ambient savepoints, PostgreSQL advisory transaction lock, additive compatibility DTOs, cursor backfill]
key-files:
  created:
    - CoffeePeek.Shops.Infrastructure.Tests/Persistence/PublicAddressServiceTests.cs
    - CoffeePeek.ShopsService.Tests/PublicAddressApiContractTests.cs
    - docs/api/public-addresses.md
  modified:
    - CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs
key-decisions:
  - Public address writes remain default-off until the live PostgreSQL transaction gate passes.
  - Existing GUID routes and response DTOs remain unchanged; canonical metadata uses separate envelopes/endpoints.
  - Existing detail caches need no slug invalidation because address and live visibility metadata are read separately from the database.
  - External frontend code owns page-level HTTP 301 responses; JSON endpoints always return data and fixed relative canonical paths.
requirements-completed: []
duration: approximately 25min
completed: 2026-09-28
---

# Phase 2 Plan 1: Shop and Roaster Public Addresses Summary

Readable shop and roaster addresses now cover supported creation/deletion flows, alias-safe lookup, admin rename/history and bounded backfill while preserving every existing GUID API contract.

## Accomplishments

- Audited the already-present implementation in `afd21ba8` instead of duplicating it. `ShopsDbContext.SaveChanges` coordinates every tracked shop/roaster addition and deletion, so admin, moderation and import creation paths share one writer. Deletion retires all canonical/alias rows before the restrictive owner FK is evaluated.
- The coordinator joins an ambient transaction through a savepoint or owns its transaction, takes a PostgreSQL transaction advisory lock, demotes/promotes in one unit and clears tracked state after rollback. Candidate allocation uses a readable stem plus deterministic bounded 8/12/16-character owner suffixes.
- Public endpoints resolve canonical and alias slugs through the live owner, suppress hidden shops/tombstones, return separate canonical metadata, batch up to 100 IDs, and retain the old GUID detail APIs. Linked roaster shops already filter hidden shops.
- Admin-only initialize, optimistic rename, history and cursor backfill endpoints use explicit actor/reason audit rows. Backfill dry-run writes nothing, collision proposals are deterministic, and replay leaves existing addresses unchanged.
- Fixed two audit findings: case-only canonical requests no longer report `isAlias: true`, and history for an unknown owner returns 404 rather than a misleading empty list.
- Added the exact API/rollout contract, frontend 301 handoff, default-off enablement gate and history-preserving rollback instructions.

## Task Commits

The broad implementation was already committed as `afd21ba8` before this execution pass. This pass added only missing fixes, checks and handoff documentation:

1. Transactional lifecycle/API behavior hardening: `860e97c1` — `fix(02-01): harden public address lifecycle`.
2. Route, compatibility, cache and authorization contracts: `2c105252` — `test(02-01): lock public address API contracts`.
3. Bounded backfill/rollout/frontend handoff runbook: `a343e0d2` — `docs(02-01): document public address rollout`.

No packages were added, no migration was changed, no production data was mutated and no tracked file was deleted.

## Verification

- Focused public-address infrastructure tests: **6 passed**, including create → rename → A→B→A restore → owner-wide retirement, case normalization, hidden/tombstone lookup, dry-run/collision/cursor/replay and unknown-owner history.
- Focused public-address API contract tests: **3 passed**, covering explicit routes, preserved GUID endpoints, no-store policy and Admin authorization.
- Full Shops domain suite: **173 passed**, 0 failed/skipped.
- Full Shops application suite: **224 passed**, 0 failed/skipped. This includes existing admin/moderation/import creation workflows and hidden roaster-linked-shop mapping checks.
- Full Shops infrastructure suite: **53 passed**, 0 failed/skipped.
- Full ShopsService suite: **11 passed**, 0 failed/skipped.
- `dotnet build CoffeePeek.Backend.ci.slnf -c Debug --no-restore`: passed with 0 errors. Existing analyzer/nullability warnings and NU1902/NU1903 advisories remain out of scope.
- `git diff --check`: passed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Canonical lookup was marked as an alias when request casing differed**
- **Found during:** Task 2 audit
- **Issue:** Lookup normalized the database key but compared the unnormalized request to the canonical slug.
- **Fix:** Compare the normalized request and add a regression assertion.
- **Files modified:** `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs`, `CoffeePeek.Shops.Infrastructure.Tests/Persistence/PublicAddressServiceTests.cs`
- **Commit:** `860e97c1`

**2. [Rule 1 - Bug] Unknown history owner returned an ambiguous empty result**
- **Found during:** Task 2 audit
- **Issue:** Admin history could not distinguish an existing owner with no later events from a nonexistent owner.
- **Fix:** Reuse the live owner lookup and return the specified 404.
- **Files modified:** `CoffeePeek.Shops.Persistance/PublicAddresses/PublicAddressService.cs`, `CoffeePeek.Shops.Infrastructure.Tests/Persistence/PublicAddressServiceTests.cs`
- **Commit:** `860e97c1`

**3. [Rule 3 - Blocking] Phase implementation existed under the preceding commit while planning state was stale**
- **Found during:** Initial execution audit
- **Issue:** Reimplementing the plan would duplicate working code and create conflicting endpoints/types.
- **Fix:** Audited every plan criterion and call path against `afd21ba8`, then added only missing tests, two root-cause fixes and documentation.

## Infrastructure Gate / Deferred Verification

Live PostgreSQL concurrency, uniqueness, restrictive-FK and rollback/savepoint verification was **not run**. Docker CLI is installed, but `docker info` reports that `//./pipe/docker_engine` does not exist (Docker daemon unavailable). EF InMemory tests verify orchestration behavior but cannot prove PostgreSQL constraints, advisory locking or transactional rollback. `PublicAddresses__Enabled` therefore remains false by default; the exact live gate is documented in `docs/api/public-addresses.md`.

The external frontend is outside this repository. Real page-level alias/GUID HTTP 301 behavior, canonical tags, links and sitemaps remain its handoff; backend JSON does not redirect.

## Known Stubs

None. Null initializers in EF materialized entities and nullable test parameters are not user-facing stubs.

## Self-Check: PASSED

All reported files exist and implementation commits `afd21ba8`, `860e97c1`, `2c105252` and `a343e0d2` are present in git history. The only unrelated working-tree item is the user's untracked `.claude/settings.local.json`; it was not modified or staged.
