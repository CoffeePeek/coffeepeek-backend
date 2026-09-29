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
| 5 | 0/8 planned | Live PostgreSQL gate open until 05-07 records a green run; feature default-off |
| 6 | Not started | Pending |

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

## Phase 5: Operational backfill, compatibility and rollout verification

Goal: close the live PostgreSQL enablement gate for phases 1–4 with an opt-in, repeatable integration suite that runs against a provided non-production PostgreSQL server (throwaway databases created and dropped per run; no Docker dependency): real migrations apply cleanly, unique/filtered indexes, RESTRICT FKs, check constraints, advisory-lock serialization, savepoint rollback inside ambient transactions, and backfill dry-run/cursor/replay behave as documented for Shops (shops, roasters, cities, zones) and Account (users). Produce a rollout runbook step for dry-run backfill review. No production data is touched and no flag is enabled in any deployed environment without explicit authorization.

Depends on: Phase 4

Plans: 8 (waves: 1 = 05-01, 05-02; 2 = 05-08; 3 = 05-03, 05-04, 05-05, 05-06; 4 = 05-07)
- [ ] 05-01-PLAN.md — opt-in live PostgreSQL harness: COFFEEPEEK_LIVE_PG gate and skip, safety guards, throwaway `cp_live_*` DB lifecycle, `LiveDatabase<TContext>`, pg_locks/transactionid probes, Account csproj link.
- [ ] 05-02-PLAN.md — Shops write-mode backfill in-flight conflict -> 409 (D-05) and the runbook: live gate, dry-run backfill review loop, manual HTTP smoke, rollback rehearsal.
- [ ] 05-03-PLAN.md — Shops live gate tests G-S0..G-S9: migrations, checks/indexes, RESTRICT FKs, advisory-lock serialization, rename demotion/rollback, ambient savepoint, city-delete 409, zone lifecycle.
- [ ] 05-04-PLAN.md — Shops backfill and rollback rehearsal: pre-feature migration + legacy seed, page-by-page dry-run/write/replay for four kinds, cross-page caveat, in-flight 409, flag-off rollback (G-S0 incremental, G-S10, G-S10b, G-S11).
- [ ] 05-05-PLAN.md — Account live gate tests G-A0..G-A7: migrations, UserPublicAddresses checks/indexes/RESTRICT FK, lock 7402100401, rename demotion ordering, savepoint rollback (username, in-flight slug, duplicate email), Google retry after ChangeTracker.Clear().
- [ ] 05-06-PLAN.md — Account backfill and rollback rehearsal: pre-feature migration + legacy users, cursor paging, blocked/deleted rules, transliteration collisions, in-flight 409, flag-off rollback (G-A0 incremental, G-A8, G-A9).
- [ ] 05-07-PLAN.md — the live gate run against a user-provided non-production PostgreSQL 17 (checkpoint), strict `Failed: 0, Skipped: 0` validation with recomputed totals, evidence recorded in docs/api/public-addresses.md (D-04, D-08), and findings classified as test or production defects.
- [ ] 05-08-PLAN.md — (wave 2, runs before 05-03..05-06) optional early checkpoint: the user attests a non-production PostgreSQL 17 server or replies "later"; if attested, the harness self-tests run live with filtered output and harness defects are fixed; records the one-time attestation that development live runs in 05-03..05-06 require.
