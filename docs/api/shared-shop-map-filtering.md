# Shared shop filtering for list and map

Implementation checkpoint: 2026-10-29, branch `codex/shared-shop-map-filters`,
based on main `5617fd5b9ecbd94d6ddbeaeef568d1c185285727`.

## Contract and ownership

Discovery owns one shop eligibility query. `POST /api/v1/coffee-shops/search`
keeps its current paginated card response and sorting. The additive
`POST /api/v1/coffee-shops/map` accepts the same `q` and `ShopFilters` with
required bounds and integer zoom (0–22):

```json
{
  "q": "",
  "filters": { "tags": ["laptop-friendly"] },
  "bounds": { "minLat": 53.88, "minLon": 27.50, "maxLat": 53.92, "maxLon": 27.60 },
  "zoom": 15
}
```

Filter slugs must come from the active catalog; the example tag is illustrative.
The response retains `Response<PublicMapDto>` with shops, clusters, zones and
`isTruncated`. Coordinates remain part of the map response only. The request
and response schemas live in `docs/contracts/discovery-coffee.openapi.yaml`;
the request DTOs are generated with `dev/generate-discovery-dtos.py`.

Both endpoints use the same visibility, tag/catalog, price/type/rating,
open/new, visited/favourite, menu and origin/radius predicates. Normalized text
matching and the existing bounded fuzzy fallback are shared. Map bounds apply
after eligibility and fuzzy selection; list sorting/pagination are not applied
to the map. Nearby origin belongs to the user, independent of the map camera.
Personal filters require an authenticated user and are never shared across
users by a response cache.

The shared map reader retains existing map geometry, membership overrides and
limits. New filtered zone counts include eligible shops throughout the zone,
including eligible zone members outside the viewport; empty filtered zones
are omitted. Clusters use the eligible viewport subset. Truncation remains
explicit. Legacy `GET /api/Map` retains its previous contract and behaviour.

No schema migration, new application module or Wolverine handler is required.
Gateway routing adds the new POST endpoint. Publish the backend before shipping
the mobile client that calls it; mobile must not silently fall back to an
unfiltered legacy map response.

## Verification plan

- Build the affected service and gateway.
- Check generated DTOs against the OpenAPI source.
- Exercise HTTP required fields, ranges, unknown fields/slugs, personal 401s
  and the unchanged legacy route.
- Compare list and marker eligibility with the same filters and text, including
  personal isolation, radius intersected with viewport and visibility rules.
- Verify filtered clusters/zones, membership overrides and response/source caps.
- Execute real PostgreSQL query tests for normalized/fuzzy text, joins and
  advanced filter translation; in-memory tests alone are insufficient.
- Run existing relevant discovery/map/service regressions. Keep production
  accounts/data untouched and retain any initial failures with their repair.

## Verification results — 2026-10-29

Source: the base revision above plus this uncommitted patch, on Windows with
.NET SDK 10.0.400. ShopsService and Gateway Debug builds passed. The generated
DTO check, schema reference review and `git diff --check` passed. No dependencies,
database migrations or generated Wolverine handlers changed.

| Executed suite | Passed | Failed / skipped |
|---|---:|---:|
| Discovery HTTP contracts | 75 | 0 / 0 |
| Gateway tests | 111 | 0 / 0 |
| Shops discovery/map regressions | 137 | 0 / 0 |
| Total, without repeated runs | 323 | 0 / 0 |

The Shops result includes 48 real PostgreSQL tests and 89 unit/query-translation
tests. New coverage comprises 24 HTTP, 12 in-memory and 7 PostgreSQL cases;
the eligibility matrix checks 19 filter combinations. It also covers normalized
name/address search, fuzzy fallback before viewport filtering, user isolation,
membership overrides, filtered zone/cluster counts and truncation.

Real SQL tests used a dedicated loopback PostgreSQL 17.11 instance and the
repository's disposable `cp_live_*` database harness. Docker Desktop could not
start, so portable PostgreSQL was used. Initial `C` locale failed existing and
new Cyrillic assertions; ICU alone fixed case folding but not pg_trgm's libc
character classification. The final test instance used ICU en-US and libc
LC_CTYPE en-US, with UTF-8. Assertions were retained unchanged.

Initial compile failures were missing imports in extracted production/test
files and an ambiguous test enum; all were fixed. Initial test fixtures also
used an oversized zone, JSON geometry through the in-memory provider and an
insert date overwritten by the audit interceptor. The zone now respects the
domain limit, geometry is tested on PostgreSQL, and historical dates are set
after insertion. Original failed logs remain local alongside passing reports.
No failure was suppressed or converted to a skip.

Nullable/XML-documentation and existing test-analyzer warnings remain. Tests
used synthetic data only; no production account, data or endpoint was changed.
Device review and acceptance against a deployed backend remain release gates.

## Develop integration — 2026-10-29

Integrated develop `26b3512a` into the feature branch at `44f11c1b`.
Discovery HTTP contracts passed again (75/75). The first PR CI run exposed an
existing ownership migration test that rolled back later migrations before
checking the original ownership guard, thereby removing `EvidencePhotoIds`.
The test now executes the original ownership migration's generated Down script
directly. All original post-failure claim, evidence-photo and historical-data
assertions remain, with an additional unchanged-migration-history assertion.
This verifies that migration's guard, not atomic rollback of a whole chain;
production migrations are unchanged. The original CI failure is retained.

On the integrated tree plus that test correction, the ownership migration test
and seven filtered-map PostgreSQL cases passed (8/8, no skips) on a new isolated
PostgreSQL 17.11 cluster with ICU/libc en-US and UTF-8. PR CI supplies the final
full integration result.

## Production promotion — 2026-10-29

The mobile develop client used the new map route while production still ran
main `47c3c552`, which did not contain it. Anonymous read-only checks against
the configured public API returned 404 for both default and filtered POST map
requests. The list with the same specialty/rating filters returned 200 with
nine matches, and the legacy GET map returned 200 with 296 shops in the checked
Minsk viewport. This identified a release-order mismatch rather than missing
shop data or a marker-rendering failure.

The production release branch integrates develop `abe9773f` onto that main
revision, preserving the existing ownership changes. It also adds
`DiscoveryMapQueryLiveTests` to CI's explicit PostgreSQL gate so its seven map
cases must execute rather than remain skipped during the unconfigured unit
pass. Production publication remains the existing main-only CI/CD path.

On the integrated release tree, .NET 10.0.113 Release verification passed:
75 Discovery HTTP contracts, 111 Gateway tests, 89 discovery/map persistence
tests and four Python OpenAPI/generated-DTO contracts. No selected test failed
or skipped. DTO generation consistency, the complete publishing-history secret
scan and `git diff --check` passed. Existing nullable/XML-documentation and
xUnit analyzer warnings remain. PostgreSQL execution and the full service build
are verified by release PR CI; these local results do not claim either.

After main publication, verify the public POST map both without filters and
with the list's identical criteria, at marker and clustered zooms. Keep the
legacy GET compatible and ensure personal filters still return 401 without
authentication. No production account writes or mobile changes are required.
