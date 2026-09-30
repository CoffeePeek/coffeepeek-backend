# Phase 4: Public profiles in Account - Context

**Gathered:** 2026-09-28
**Status:** Ready for planning
**Source:** Research open questions answered by user (Q1–Q4); Q5–Q9 take the research recommendation

<domain>
## Phase Boundary

Readable stable public profile addresses for Account users inside the Account bounded context, reusing shared `PublicSlug` rules and the `PublicAddress` base. GUID routes/DTOs unchanged; default-off behind the live PostgreSQL gate. External frontend (phase 6) out of scope.

</domain>

<decisions>
## Implementation Decisions

### Slug policy
- D-01: Slug follows the username. Allocate at user creation (email and Google) from the username; a username change is an owner-authorized rename: old slug becomes an alias, A→B→A restores, case- or punctuation-only changes that generate the same slug do nothing.
- D-02: Slug squatting via repeated username changes (each reserves one slug permanently) is recorded as an enablement-gate item; no cooldown this phase.

### Visibility
- D-03: A profile is public iff not soft-deleted and not blocked. No new privacy flag. Email-unconfirmed users stay visible. Block hides without retiring; soft/hard delete always retires all addresses (even when the feature is disabled). Tombstones carry no private profile data.
- D-04: Fix the existing leak: `GET api/Users/{id:guid}` returns 404 for soft-deleted and blocked users (body-only handler change, same DTO, no Wolverine codegen change).

### Reuse
- D-05: Lean Account-local single-kind implementation in Account.Persistence (no Shops edits), with schema model tests pinning parity with the Shops registries.

### Defaults (research recommendations)
- D-06: No Account audit table and no admin history endpoint; registry rows are the history.
- D-07: Dedicated gateway route for `/api/Users/by-slug/{slug}` on the existing `auth-endpoints` rate limit (avoids Shops `/api/users/{userId}/reviews` capture). Batch and legacy-GUID stay on the existing catch-all.
- D-08: Canonical path `/users/{slug}`.
- D-09: Email-derived Google usernames becoming crawlable is accepted and documented (frontend may `noindex`).
- D-10: Admin scope is backfill only (dry-run, cursor, idempotent replay).

### Claude's Discretion
- File split of tests, exact service shape, migration name (generated via `make mig-acc`).

</decisions>

<canonical_refs>
## Canonical References

- `.planning/specs/PUBLIC-SLUGS.md` — approved staged spec (step 5)
- `.planning/phases/04-public-profiles-in-account/04-RESEARCH.md` — evidence, pitfalls, validation architecture
- `.planning/phases/03-cities-zones-and-catalog-filters/03-REVIEW.md` — prior review findings to avoid repeating (vacuous tests, migration-as-deploy-prerequisite)
- `docs/api/public-addresses.md` — API/rollout contract to extend

</canonical_refs>

<deferred>
## Deferred Ideas

- Opt-out profile privacy flag.
- Username-change cooldown / anti-squatting limit.
- Account audit/history of slug changes.
- Admin initialize/rename for users.

</deferred>
