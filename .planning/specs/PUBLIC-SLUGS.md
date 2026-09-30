# Public slug rollout

Reconstructed on 2026-09-27 from the approved conversation and phase-1 plan: the earlier `/tmp/CoffeePeek-public-slugs-plan.md` artifact is no longer present. This is the persistent staged specification, not a claim of completed rollout.

## Product behavior

Public pages use readable stable slugs. GUID primary keys, relationships, events and existing API routes remain supported. Initial scope is CoffeeShop and Roaster. Later phases assess public catalogs, cities, coffee zones and Account profiles; service-only resources such as uploads and check-ins do not automatically need slugs. There is no frontend in this repository.

Slugs contain lowercase ASCII letters, digits and single interior hyphens, maximum 100 characters. Generation transliterates Russian/Belarusian and Latin names deterministically. Strict manual validation rejects invalid input, reserved route words and GUID-shaped strings. Generated unsupported/empty/reserved names fall back to `place`; deterministic owner-derived suffixes handle conflicts. The database is the uniqueness authority. Generation rules must not retroactively rewrite published addresses.

Editing a display name does not rename a URL. Explicit authorized rename creates or restores the requested address and retains previous addresses. A→B→A is supported for the same entity, while another entity cannot claim A. Slug namespaces are per entity type, including canonical addresses, aliases and permanently reserved tombstones. Aliases resolve directly through the owner to its current canonical address, avoiding redirect chains.

## Persistence and lifecycle

Use typed registries with real foreign keys, initially CoffeeShopPublicAddresses and RoasterPublicAddresses. Each row has an ID, nullable owner ID, immutable slug, state (canonical/alias/tombstone), revision and UTC creation/update timestamps. Tombstones remove the owner reference but retain the reserved slug. Do not copy private profile data into future Account tombstones.

Unique slug indexes reserve every state. A filtered unique owner index ensures **at most one** canonical address. The phase-2 transactional coordinator ensures **at least one** for a published entity. Positive revisions are concurrency tokens; owner-wide serialization and globally monotonic rename revisions remain coordinator responsibilities. Rename demotes the current row before promoting/creating the replacement inside one transaction, rolling back both steps on failure. Restoring an alias requires checking the current owner's revision rather than relying only on the alias revision.

Restrictive FKs prevent deletion while active addresses or aliases exist. Before physical deletion, retire **all** owner addresses transactionally, then delete the owner. Entities without registry entries retain existing deletion behavior. Phase 1 does not populate registry entries or modify production deletion handlers. Before enabling writers/backfill, phase 2 must integrate deletion coordination. Direct SQL can bypass lifecycle methods; supported writes must go through the coordinator.

## API, redirects and rollout

Preserve GUID API behavior. Add non-conflicting slug lookup routes returning JSON 200 for canonical and historical slugs, with canonical metadata and resolved entity ID. Visibility checks must match existing public entity rules. Tombstones/unknown/private resources must not leak private information. Additive DTO changes need compatibility review; breaking contracts need versioning.

The external frontend owns permanent page redirects from GUID URLs and aliases, canonical tags, links and sitemaps. API lookup is not a browser-page redirect. Redirect targets must be server-derived paths. Invalidate owner/alias caches on committed changes; test repeated renames and rollback.

## Implementation sequence

1. Shared rules, typed shop/roaster registries, lifecycle tests and additive generated migration.
2. Transactional allocation/rename/restore/retirement, authorization, optimistic concurrency, race tests and writer integration.
3. Shop/roaster slug lookup and canonical metadata, visibility checks, cache invalidation and compatibility tests.
4. Idempotent resumable backfill, collision reporting, dry-run and controlled enablement; no production application without deployment authorization.
5. Extend to approved catalogs/cities/zones, then Account with profile privacy and username policy.
6. External frontend implementation, permanent page redirects, canonical tags and links.
7. End-to-end checks, observability, runbook and rollback rehearsal. Rollback disables new readers/writers while retaining registry history; dropping populated registries loses reservations.

## Verification

Test transliteration, normalization, bounds, reserved/GUID-shaped values and stable candidates. Test lifecycle and prohibited transitions. Inspect generated migration/snapshot for unrelated drift. Real PostgreSQL tests must cover duplicate allocation, canonical uniqueness, alias restoration, retirement/FK behavior and rollback; InMemory cannot prove constraints. Record missing infrastructure explicitly. Full completion requires working public pages and external frontend redirects; phase 1 alone does not change public links.
