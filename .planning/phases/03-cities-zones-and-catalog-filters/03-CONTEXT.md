# Phase 3: Cities, zones and catalog filters - Context

**Gathered:** 2026-09-28
**Status:** Ready for planning
**Source:** Research open questions answered by user (plan-phase)

<domain>
## Phase Boundary

Readable stable public addresses for City and published CoffeeZone, reusing the phase 1–2 slug rules, registries, coordinator, lookup, rename/history and backfill. Existing GUID routes/DTOs unchanged; default-off behind the live PostgreSQL gate. Account profiles (phase 4) and the external frontend (phase 6) are out of scope.

</domain>

<decisions>
## Implementation Decisions

### Namespace
- D-01: Coffee zones use one global per-type namespace (canonical `/coffee-zones/{slug}`), not nested per city. City namespace is separate (canonical `/cities/{slug}`).

### Visibility
- D-02: Every existing city is public by slug (matches unfiltered `GET /api/Catalogs/cities`). A coffee zone is public only while Published; Draft/Archived resolve as not found.

### Catalog scope
- D-03: Only City and CoffeeZone get registries. ShopTag keeps its existing immutable unique slug (no registry). BrewMethod, CoffeeBean and Equipment are deferred.
- D-04: No slug-based filter query params on search/map endpoints this phase; GUID filter contracts unchanged. Frontend resolves slug → GUID through the new lookup endpoints. Document stable filter values (tag slug, city/zone/roaster via address endpoints).

### CI
- D-05: Add `CoffeePeek.ShopsService.Tests` to `CoffeePeek.Backend.ci.slnf` so route/contract and Wolverine codegen tests run in CI.

### Claude's Discretion
- Exact file split of new tests, service method shapes, admin route regex, migration name (generated via `make mig-shops`).

</decisions>

<canonical_refs>
## Canonical References

- `.planning/specs/PUBLIC-SLUGS.md` — approved staged spec (step 5)
- `.planning/phases/03-cities-zones-and-catalog-filters/03-RESEARCH.md` — evidence, patterns, pitfalls, validation architecture
- `.planning/phases/02-shop-roaster-public-addresses/02-01-SUMMARY.md` — what phase 2 built
- `docs/api/public-addresses.md` — API/rollout contract to extend

</canonical_refs>

<deferred>
## Deferred Ideas

- Public-address registries for BrewMethod, CoffeeBean, Equipment.
- Slug-based filter params on search/map.
- Nested per-city zone URLs.

</deferred>
