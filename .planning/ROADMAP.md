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
| 4–6 | Not started | Pending |

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
