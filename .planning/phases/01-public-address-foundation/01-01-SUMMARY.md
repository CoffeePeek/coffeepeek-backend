---
phase: 01-public-address-foundation
plan: 01
subsystem: database
tags: [slugs, dotnet, ef-core, postgresql]
requires: []
provides:
  - Deterministic public slug generation and strict manual validation
  - Typed shop/roaster canonical, alias and tombstone lifecycle
  - Additive EF registries with unique namespaces and restrictive owner FKs
affects: [public-address-writers, public-address-resolution, backfill, public-frontend]
tech-stack:
  added: []
  patterns: [typed registries, filtered canonical uniqueness, revision concurrency tokens]
key-files:
  created:
    - CoffeePeek.Shared.Domain/PublicAddresses/PublicSlug.cs
    - CoffeePeek.Shared.Domain/PublicAddresses/PublicAddress.cs
    - CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/CoffeeShopPublicAddress.cs
    - CoffeePeek.Shops.Domain/Aggregates/PublicAddressAggregate/RoasterPublicAddress.cs
    - CoffeePeek.Shops.Persistance/Configuration/PublicAddressConfiguration.cs
    - CoffeePeek.Shops.Persistance/Migrations/20260927112559_AddPublicAddressRegistries.cs
  modified:
    - CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs
    - CoffeePeek.Shops.Persistance/Migrations/ShopsDbContextModelSnapshot.cs
key-decisions:
  - Restrictive FKs require retiring every address before deleting an owner.
  - Tombstones discard owner references while permanently reserving slugs.
  - Canonical uniqueness is at-most-one; publication and rename transactions must supply at-least-one.
  - Owner-wide revision ordering is deferred to the phase-2 coordinator.
requirements-completed: []
duration: approximately 12min
completed: 2026-09-27
---

# Phase 1 Plan 1: Public Address Foundation Summary

Deterministic RU/BY/Latin slugs and reversible alias lifecycle now have typed PostgreSQL registries, permanent slug reservations and an additive generated migration.

## Accomplishments

- Shared policy generates lowercase bounded ASCII, normalizes combining accents, transliterates Cyrillic, falls back safely and builds deterministic owner-derived conflict candidates. Manual input is strictly validated, including reserved routes, GUID shapes and final newline rejection.
- Shop and roaster address types support canonical→alias→canonical and irreversible retirement. Immutable ownership cannot be transferred; retirement clears the FK. Revisions increase on each row transition; timestamps must be UTC and nondecreasing.
- Separate tables retain aliases/tombstones under the same unique slug namespace. Filtered unique owner indexes enforce at most one canonical; lifecycle, revision and timestamp checks protect stored shape. Revision is an EF concurrency token.
- Existing public controllers, DTOs and entity creation/deletion paths are untouched. No addresses are allocated yet and no database migration was applied.

## Task Commits

1. Stable public slug rules and lifecycle: `7aba62c2` — `feat(01-01): add public slug policy and address lifecycle`.
2. Typed PostgreSQL registries and additive migration: `0e562a2a` — `feat(01-01): add typed public address registries and migration`.
3. Verification and stage handoff: this summary, `01-VERIFICATION.md`, deferred items and restored persistent spec; planning metadata commit belongs to the orchestrator together with STATE/ROADMAP.

13 production/test/migration files changed across the two implementation commits. No tracked files deleted. No packages installed. No push.

## Verification

- Focused domain tests: **28 passed**. One first-run failure correctly exposed the regex `$` final-newline loophole; changed to absolute `\z` and reran successfully.
- Focused EF model tests: **2 passed**, one per typed registry.
- Full Shops domain suite: **173 passed**, 0 failed/skipped.
- Full Shops infrastructure suite: **49 passed**, 0 failed/skipped.
- `make mig-shops n=AddPublicAddressRegistries`: passed, service build passed.
- `dotnet ef migrations script` from `20260923120247_AddCoffeeZonePolygon` to the new migration: passed after rebuilding the startup host, SQL inspected; only two new tables, their constraints/indexes and migration history insertion.
- `dotnet ef migrations has-pending-model-changes`: **no pending changes**.
- Snapshot diff contains only the two new types and their relationships. `git diff --check` passed.
- No TODO/FIXME/placeholder stubs in new source/tests. EF-only `Slug = null!` initialization is materialization infrastructure, not a runtime feature stub.

## Deviations from Plan

- **Rule 3 — missing persistent spec:** `.planning/specs/PUBLIC-SLUGS.md` contained only a missing `/tmp` file error. Reconstructed the approved staged scope and rules with orchestrator authorization.
- **Rule 3 — tooling environment:** installed dotnet-ef was missing from PATH; prefixed `/home/arseny/.dotnet/tools` for commands. .NET needed escalation for its existing home/tool cache. No installations or repository tool configuration changes.
- **Rule 1 — validation bug found during implementation:** strict end-of-input anchoring rejects trailing newline; included in task-1 commit and regression cases.

## Deferred Issues / Infrastructure Gate

Live PostgreSQL constraint and transaction tests were **not run**. Docker socket is absent, psql is unavailable, and approved `sudo -n dockerd` returned `sudo: a password is required`. SQL generation and EF model tests do not substitute for live uniqueness/FK/concurrency tests. This does not prevent committing additive dormant schema; it blocks claiming production readiness and must be closed before writer/backfill enablement.

Existing nullable/analyzer warnings and vulnerability warnings for Microsoft.OpenApi 2.0.0 (NU1903) and OpenTelemetry.Api 1.15.0 (NU1902) remain out of scope. No full solution suite or fresh vulnerability audit was run in this stage.

## Next Phase Readiness

Implement owner-serialized transactional allocation/rename/restore and enforce owner-wide revision checks. Demote canonical before promoting another row, with rollback of the whole transaction on any uniqueness or concurrency failure. Always retire all aliases and canonical rows before owner deletion. Wire these paths before enabling any registry population. Reserved-word/GUID exclusion and monotonic row transitions are domain guarantees; raw SQL writes can bypass them, so supported writers must use the coordinator.

Then add authorized APIs, visibility-aware resolution, canonical metadata, cache invalidation and resumable backfill. External frontend page redirects and later entities are pending. The current feature does **not** yet change any public URL or expose slug lookup.

## Self-Check: PASSED

Both implementation commits verified in git log; all reported source, test and generated migration files exist. Summary/verification/spec files created. Working tree outside orchestrator planning artifacts is clean. STATE/ROADMAP updates are explicitly delegated to the orchestrator.
