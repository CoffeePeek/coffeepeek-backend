# Public addresses

This backend slice adds readable addresses for coffee shops, roasters, cities and coffee zones without changing existing GUID routes or DTOs. It is disabled by default and has not been run against production data.

## Deployment prerequisite (independent of the flag)

Apply the Shops migrations `20260927112559_AddPublicAddressRegistries`, `20260928072406_AddPublicAddressAudit` and `20260928082712_AddCityAndZonePublicAddresses` **before** this build starts serving traffic, even with `PublicAddresses__Enabled=false`. Deleting a coffee shop, roaster or city always retires its registry rows, so without these tables those deletes fail with 500. `deploy/scripts/ci-deploy.sh` and `deploy/scripts/deploy.sh` apply migrations before restarting; do not deploy with `SKIP_MIGRATIONS=1`. `deploy/scripts/update.sh` restarts without migrating, so run `deploy/scripts/apply-migrations.sh` first when using it.

## Enablement gate

1. Confirm the deployment prerequisite migrations above are applied in the target environment.
2. Run the pending live PostgreSQL tests for uniqueness, restrictive foreign keys, concurrent allocation/rename and transaction/savepoint rollback. Cities and coffee zones add the checks below. They are still pending: Docker/PostgreSQL was unavailable, so none of them has been verified.
   - The unique `Slug` index and the filtered canonical owner index on `CityPublicAddresses` and `CoffeeZonePublicAddresses` hold under concurrent allocation. Two concurrent "Минск" creations serialize on the advisory lock.
   - `FK_CityPublicAddresses_Cities_OwnerId` and `FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId` are RESTRICT: deleting a city without retiring its addresses fails.
   - Deleting a city that still has coffee zones returns 409, and its address retirement rolls back.
   - `CK_PublicAddressAudit_Kind` accepts kinds 3 (city) and 4 (coffee zone).
   - `CK_CityPublicAddresses_Lifecycle` and `CK_CoffeeZonePublicAddresses_Lifecycle` reject a canonical row without an owner.
3. Set `PublicAddresses__Enabled=true` for ShopsService and restart it.
4. Run dry-run backfill pages and review collisions before running write mode.

Do not enable writers or execute write-mode backfill before steps 1–2 pass. When disabled (and the prerequisite migrations are applied), new endpoints return 503 and new entities are not assigned addresses; existing GUID APIs are unchanged.

## Public API

| Entity | Prefix | `data` | Canonical path |
| --- | --- | --- | --- |
| Coffee shop | `api/CoffeeShops` | Shop details | `/coffee-shops/{slug}` |
| Roaster | `api/roasters` | Roaster details | `/roasters/{slug}` |
| City | `api/Catalogs/cities` | `CityDto` `{id, name}` | `/cities/{slug}` |
| Coffee zone | `api/Catalogs/coffee-zones` | `MapCoffeeZoneDto`, the same shape as `/api/Map` zones | `/coffee-zones/{slug}` |

| Method | Path | Result |
| --- | --- | --- |
| GET | `/{prefix}/by-slug/{slug}` | Entity data plus canonical address metadata |
| GET | `/{prefix}/public-addresses?ids={guid}&ids={guid}` | Up to 100 visible canonical addresses |
| GET | `/{prefix}/{id}/public-address` | Address for one legacy GUID |

Canonical and alias lookups both return HTTP 200 JSON. Unknown, hidden and retired owners return 404. Input is matched case-insensitively and metadata always contains the normalized canonical slug.

Every city is public: the same set as `GET api/Catalogs/cities`. A coffee zone is public only while Published. Draft, Archived, unknown and retired zones all return 404 and are omitted from batch results. The zone by-slug endpoint checks the status again when it reads the zone data, so a zone archived between the two reads also returns 404.

```json
{
  "data": { "id": "11111111-1111-1111-1111-111111111111" },
  "address": {
    "entityId": "11111111-1111-1111-1111-111111111111",
    "slug": "surf-coffee-oktyabrskaya-16",
    "canonicalPath": "/coffee-shops/surf-coffee-oktyabrskaya-16",
    "revision": 2,
    "isAlias": true
  }
}
```

City example (`GET api/Catalogs/cities/by-slug/minsk`):

```json
{
  "data": { "id": "22222222-2222-2222-2222-222222222222", "name": "Минск" },
  "address": {
    "entityId": "22222222-2222-2222-2222-222222222222",
    "slug": "minsk",
    "canonicalPath": "/cities/minsk",
    "revision": 1,
    "isAlias": false
  }
}
```

`canonicalPath` is a server-built relative page path. The API never redirects JSON and never accepts a redirect target from the request. Existing `GET api/CoffeeShops/{id:guid}` and `GET api/roasters/{id:guid}` contracts remain unchanged. The existing `GET api/Catalogs/cities` list and `GET api/Map` payloads are also unchanged and carry no address metadata.

The new metadata is not stored in the existing shop/roaster detail caches: resolution reads the live registry and visibility from the database, then invokes the unchanged detail query. Therefore address changes require no invalidation of those payload caches.

City and zone address metadata is not stored in the `city:list:all`, map or search caches either. Lookups read the registry and visibility live, and the city by-slug data is read from the database rather than from `city:list:all`, so no cache invalidation is needed.

## Cities and coffee zones

Each kind has one global namespace (cities, coffee zones, shops and roasters are independent, so `minsk` can exist in all four). Zone slugs are unique across all cities, not per city. A same-name zone in another city therefore gets a deterministic short suffix derived from its GUID. Admins can choose a different slug with initialize or rename.

Allocation rules:

- A city gets an address when it is created. A coffee zone gets one on its first publication; drafts get no automatic address.
- Display-name edits never rename an address.
- Archiving a zone or moving it back to Draft hides it without retiring its address. Republishing reuses the same address.
- An admin may initialize a Draft zone's slug before publishing it. Publication keeps that slug.
- Deleting a city retires all its addresses, even when the feature is disabled. Zones are archived, not deleted.

## Admin API

All endpoints require the Admin policy. `{entity}` is exactly `shops`, `roasters`, `cities` or `coffee-zones`; any other value is rejected. The gateway forwards `/api/admin/cities/**` and `/api/admin/coffee-zones/**` for Moderators, but ShopsService still enforces the Admin policy on these endpoints.

| Method | Path |
| --- | --- |
| POST | `api/admin/{entity}/public-addresses/{id}/initialize` |
| PATCH | `api/admin/{entity}/public-addresses/{id}` |
| GET | `api/admin/{entity}/public-addresses/{id}/history?afterRevision=0&limit=50` |
| POST | `api/admin/{entity}/public-addresses/backfill?afterId={cursor}&limit=50&dryRun=true` |

Initialize/rename body:

```json
{
  "slug": "surf-coffee-center",
  "expectedRevision": 2,
  "reason": "Use the public brand name"
}
```

Initialization requires revision `0`; `slug` may be null to generate it. Rename requires a canonical lowercase slug and the current positive revision. Responses use 400 for invalid input, 404 for an unknown owner and 409 for stale revision or a reserved address. Historical aliases remain owned by the entity and resolve directly to its current canonical address.

## Bounded backfill

Backfill is ordered by owner GUID, accepts 1–100 rows and returns `nextCursor` when another page exists. Always start with `dryRun=true`; it performs no writes and reports each proposed slug, whether a collision suffix was needed and whether the owner was already initialized.

Repeat the same dry-run request if desired, then submit it with `dryRun=false`. A replay never renames initialized owners. Continue with the returned cursor until it is null. Each entity is a separate run. No production backfill is part of this repository change.

City backfill includes every city; coffee zone backfill includes only Published zones. Review the city dry-run output for:

- Duplicate city names. `City.Name` is not unique, so the second city (in GUID order) gets a suffix.
- RU/BY spelling variants, which produce different slugs: `Гродно` -> `grodno` vs `Гродна` -> `grodna`, and `Могилёв` -> `mogilyov` vs `Магілёў` -> `magilyou`.

Where the generated slug is not the one you want, choose a manual slug with initialize before running write mode.

## Catalog filter values

Search and map query contracts are unchanged and GUID-based. No slug query parameters exist. The external frontend composes readable page and filter URLs, then resolves them to GUIDs before calling `GET api/CoffeeShops`.

| Filter | Query param | Readable value source |
| --- | --- | --- |
| City | `cityId` | `GET api/Catalogs/cities/by-slug/{slug}` (GUID in `data.id`); reverse mapping via `GET api/Catalogs/cities/public-addresses?ids=` |
| Roaster | `roasters` | `GET api/roasters/by-slug/{slug}`; reverse mapping via `GET api/roasters/public-addresses?ids=` |
| Tag | `tags` | `GET api/Catalogs/shop-tags` returns `{id, slug}` for each active tag |
| Coffee zone | none | No search filter exists; zone pages use the `api/Catalogs/coffee-zones` endpoints |
| Brew method, bean, equipment | `brewMethods`, `beans`, `equipments` | GUID only; no readable value in this phase |

The tag slug is immutable and unique, but it uses underscores (for example `laptop_friendly`). It is not a public address and is not validated by public-address rules, so use it as-is. Enum and other params (`type`, `priceRange`, `isOpen`, `isNew`, `isVisited`, `minRating`, `q`, `page`, `pageSize`) keep their existing values.

## Frontend handoff and rollback

The external frontend should call by-slug, render `data`, emit a canonical tag for `address.canonicalPath`, and issue a real HTTP 301 from an alias page or legacy GUID page to that fixed relative path. It must not construct redirect targets from the requested slug. This applies to city and coffee zone pages as well.

Operational rollback is to set `PublicAddresses__Enabled=false` and keep serving the existing GUID routes. Retain the registry and audit tables so published aliases and tombstone reservations are not lost. Do not run the migration `Down` after any addresses have been allocated. In particular, the `AddCityAndZonePublicAddresses` `Down` restores `"Kind" IN (1, 2)` and drops the city and zone registries, so it must not run after any city or zone address was allocated.
