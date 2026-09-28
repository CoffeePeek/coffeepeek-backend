# Public addresses: shops and roasters

This backend slice adds readable addresses without changing existing GUID routes or DTOs. It is disabled by default and has not been run against production data.

## Enablement gate

1. Apply the Shops migration `20260927112559_AddPublicAddressRegistries` in the target environment.
2. Run the pending live PostgreSQL tests for uniqueness, restrictive foreign keys, concurrent allocation/rename and transaction/savepoint rollback.
3. Set `PublicAddresses__Enabled=true` for ShopsService and restart it.
4. Run dry-run backfill pages and review collisions before running write mode.

Do not enable writers or execute write-mode backfill before steps 1–2 pass. When disabled, new endpoints return 503 and new entities are not assigned addresses; existing GUID APIs are unchanged.

## Public API

Coffee shops use `api/CoffeeShops`; roasters use `api/roasters`.

| Method | Path | Result |
| --- | --- | --- |
| GET | `/{prefix}/by-slug/{slug}` | Entity data plus canonical address metadata |
| GET | `/{prefix}/public-addresses?ids={guid}&ids={guid}` | Up to 100 visible canonical addresses |
| GET | `/{prefix}/{id}/public-address` | Address for one legacy GUID |

Canonical and alias lookups both return HTTP 200 JSON. Unknown, hidden and retired owners return 404. Input is matched case-insensitively and metadata always contains the normalized canonical slug.

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

`canonicalPath` is a server-built relative page path. The API never redirects JSON and never accepts a redirect target from the request. Existing `GET api/CoffeeShops/{id:guid}` and `GET api/roasters/{id:guid}` contracts remain unchanged.

The new metadata is not stored in the existing shop/roaster detail caches: resolution reads the live registry and visibility from the database, then invokes the unchanged detail query. Therefore address changes require no invalidation of those payload caches.

## Admin API

All endpoints require the Admin policy. `{entity}` is exactly `shops` or `roasters`.

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

Repeat the same dry-run request if desired, then submit it with `dryRun=false`. A replay never renames initialized owners. Continue with the returned cursor until it is null. Shops and roasters are separate runs. No production backfill is part of this repository change.

## Frontend handoff and rollback

The external frontend should call by-slug, render `data`, emit a canonical tag for `address.canonicalPath`, and issue a real HTTP 301 from an alias page or legacy GUID page to that fixed relative path. It must not construct redirect targets from the requested slug.

Operational rollback is to set `PublicAddresses__Enabled=false` and keep serving the existing GUID routes. Retain the registry and audit tables so published aliases and tombstone reservations are not lost. Do not run the migration `Down` after any addresses have been allocated.
