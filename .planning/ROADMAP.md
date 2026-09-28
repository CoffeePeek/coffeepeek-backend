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
| 3–6 | Not started | Pending |

## Phase 2: Coffee shops and roasters

Goal: implement the first complete backend address flow with writer/delete integration, visibility-aware canonical resolution, authorized rename/history, bounded backfill and unchanged legacy API contracts.

Plans: 1
- [x] 02-01-PLAN.md — transactional coordinator, integrations, APIs, backfill, compatibility tests and client handoff.
