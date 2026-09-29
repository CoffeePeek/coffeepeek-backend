# Public URLs rollout

Source: specs/PUBLIC-SLUGS.md. Existing quick-task history in STATE.md is preserved.

- [ ] Phase 1: Slug rules and persistent address history (Shops foundation).
- [ ] Phase 2: Coffee shops and roasters: writers, resolver APIs, rename, backfill.
- [ ] Phase 3: Cities, zones and catalog filters.
- [ ] Phase 4: Public profiles in Account.
- [ ] Phase 5: Operational backfill, compatibility and rollout verification.
- [ ] Phase 6: Frontend routing, redirects and canonical/share links (external repository).

## Phase 1: Slug rules and persistent address history

Goal: reusable stable slug generation and a database-enforced address namespace for coffee shops and roasters, with alias ownership, tombstones and rename revision support. No production backfill or client routing change in this phase.

Plans: 1
- [x] 01-01-PLAN.md — domain rules, typed registries, EF migration and meaningful tests.

## Progress

| Phase | Plans | Status |
|---|---|---|
| 1 | 1/1 implemented | PostgreSQL verification pending |
| 2 | 1/1 implemented | PostgreSQL verification pending; feature default-off |
| 3 | 2/2 implemented | PostgreSQL verification pending; feature default-off |
| 4 | 3/3 implemented | PostgreSQL verification pending; feature default-off |
| 5–6 | Not started | Pending |

## Phase 2: Coffee shops and roasters

Goal: implement the first complete backend address flow with writer/delete integration, visibility-aware canonical resolution, authorized rename/history, bounded backfill and unchanged legacy API contracts.

Plans: 1
- [x] 02-01-PLAN.md — transactional coordinator, integrations, APIs, backfill, compatibility tests and client handoff.

## Phase 3: Cities, zones and catalog filters

Goal: extend readable stable public addresses to cities, published coffee zones and the public catalog filter values that appear in page/filter URLs, reusing the phase 1–2 slug rules, registries/coordinator, visibility-aware lookup, rename/history and bounded backfill. Existing GUID routes and DTOs stay unchanged; feature stays default-off behind the same live PostgreSQL gate. Account profiles (phase 4) and the external frontend (phase 6) are out of scope.

Depends on: Phase 2

Plans: 2
- [x] 03-01-PLAN.md — city/zone registries, audit kind check migration, lifecycle hook and explicit four-kind service dispatch with tests.
- [x] 03-02-PLAN.md — public api/Catalogs city/zone address endpoints, admin kinds, contract tests, CI filter (ShopsService.Tests), catalog filter docs and live gate.

## Phase 4: Public profiles in Account

Goal: readable stable public profile addresses for Account users, reusing the shared slug rules (`CoffeePeek.Shared.Domain/PublicAddresses/PublicSlug.cs`) and the phase 1–3 registry/lifecycle/lookup/rename/history/backfill model inside the Account bounded context, with an explicit profile privacy rule (private or deleted profiles never resolve or leak) and a username → slug policy. Account deletion must retire addresses without copying private profile data into tombstones. Existing GUID routes and DTOs stay unchanged; feature default-off behind the live PostgreSQL gate. External frontend (phase 6) out of scope.

Depends on: Phase 1, Phase 2

Plans: 3
- [x] 04-01-PLAN.md — Account-local UserPublicAddresses registry, generated migration, SaveChanges hook (allocate on create, follow username with aliases, always retire on delete) with schema/lifecycle tests.
- [x] 04-02-PLAN.md — visibility-filtered ProfileAddressService, public api/Users by-slug/batch/legacy endpoints, Admin-only backfill, GUID-route 404 fix for deleted/blocked users, contract/behavior tests.
- [x] 04-03-PLAN.md — dedicated gateway by-slug route with precedence tests, user profile docs, deployment prerequisite, extended live gate and full phase verification.
