# Public addresses: deployment and operations

The current API uses slugs for public identities, including technical catalogs. There is no second API version and no public GUID address lookup. Lists and detail responses include addresses directly. See the [client contract](public-addresses-client.md) for exact routes and JSON shapes. Internal aggregate IDs, administrative/owner APIs and message contracts remain unchanged.

## Schema prerequisite

Apply all Account and Shops migrations **before** starting this build, even with address flags disabled. Deletion retires registry rows while disabled, so missing tables can break deletion.

Required address migrations:

- Shops: `20260927112559_AddPublicAddressRegistries`, `20260928072406_AddPublicAddressAudit`, `20260928082712_AddCityAndZonePublicAddresses`, **`20260930120711_AddCatalogPublicAddresses`**.
- Account: `20260928134755_AddUserPublicAddresses`, `20260930083625_AddUsernameChangeCooldown`.

The new Shops migration creates `CoffeeBeanPublicAddresses`, `EquipmentPublicAddresses`, `BrewMethodPublicAddresses`, `ShopTagPublicAddresses`, and extends the audit kind constraint to values 1–8. It does not populate existing owners. Unique slug indexes, filtered canonical-owner indexes, lifecycle checks and restrictive foreign keys apply to all eight registries.

`deploy/scripts/ci-deploy.sh` and `deploy/scripts/deploy.sh` apply migrations before restarting. Do not use `SKIP_MIGRATIONS=1`. When using `deploy/scripts/update.sh`, run `deploy/scripts/apply-migrations.sh` first: update itself only restarts services.

## Rollout

1. Verify the build and automated gates below. This repository change has not deployed services or written production addresses.
2. Apply migrations to Account and Shops.
3. Start the coordinated Account, Shops, Moderation and Gateway build with `PublicAddresses__Enabled=true` on **both Account and Shops**. Compose exposes these as `ACCOUNT_PUBLIC_ADDRESSES_ENABLED=true` and `SHOPS_PUBLIC_ADDRESSES_ENABLED=true` in `deploy/.env`; the defaults are false.
4. Keep client traffic gated while existing addresses are populated. A visible but uninitialized shop causes the list to return 503. A slug lookup without an allocated address returns 404. Deployment alone does not complete backfill.
5. Backfill all eight Shops kinds and users, using the bounded, reviewed loop below. Review generated slugs and collision/fallback candidates before writing each page.
6. Validate through the gateway: catalogs return arrays containing slug/address; shop search accepts slug filters; details include review author addresses; map markers include addresses; authenticated check-in/review/submission writes accept slugs.
7. Switch clients together to the documented response shapes. Remove client address enrichment and GUID fallback. The API has no compatibility version.

Moderation resolves shop/city/catalog slugs through Shops; Shops resolves profile addresses through Account. Their existing named HTTP clients and the shared `GatewayAuth` secret must be configured. `/internal/public-addresses/**` requires that secret, is excluded from OpenAPI, uses no-store, and has no gateway route. Cross-service lookup failures return 503 rather than falling back to an ID.

## Backfill and administrative operations

All routes below require Admin. Shops `{kind}` is one of:
`shops`, `roasters`, `cities`, `coffee-zones`, `beans`, `equipments`, `brew-methods`, `shop-tags`.

| Method | Path |
| --- | --- |
| POST | `/api/admin/{kind}/public-addresses/backfill?afterId={cursor}&limit=100&dryRun=true` |
| POST | `/api/admin/{kind}/public-addresses/{id}/initialize` |
| PATCH | `/api/admin/{kind}/public-addresses/{id}` |
| GET | `/api/admin/{kind}/public-addresses/{id}/history?afterRevision=0&limit=50` |
| POST | `/api/admin/users/public-addresses/backfill?afterId={cursor}&limit=100&dryRun=true` |

The administrative APIs intentionally retain GUIDs. Initialize/rename accepts:

```json
{"slug":"surf-coffee-center","expectedRevision":2,"reason":"Use the public brand name"}
```

Initialize requires revision 0 and permits `slug: null` for generation. Rename requires a positive current revision and a valid lowercase slug. Invalid input returns 400, an unknown owner 404, stale revision or reserved slug 409. Users have backfill only; username changes drive their addresses.

Backfill orders owners by GUID, accepts 1–100 rows, and returns `items` and `nextCursor`. Repeat the same page with `dryRun=false` only after reviewing it; advance using the returned cursor until it is null. A replay does not rename initialized owners. Dry-run writes nothing, so write page N before previewing page N+1: otherwise collisions spanning pages are not reflected in the preview.

Run separately for each of the eight kinds and then `users`. Cities and ordinary catalogs include existing owners; coffee zones include Published zones. Tags include inactive owners to reserve their addresses permanently, but inactive tags are excluded from public reads. User backfill includes blocked users, which stay hidden, and excludes deleted users.

Operator example (Bash, token supplied privately; review files stay outside the repository):

```bash
read -rs TOKEN
E=beans
CURSOR=""
while :; do
  Q="limit=100${CURSOR:+&afterId=$CURSOR}"
  curl -sS --fail-with-body -X POST "$GW/api/admin/$E/public-addresses/backfill?$Q&dryRun=true" \
    -H "Authorization: Bearer $TOKEN" > dry.json || break
  jq '.items[]' dry.json
  read -rp "Write reviewed page? [y/N] " OK
  [ "$OK" = y ] || break
  curl -sS --fail-with-body -X POST "$GW/api/admin/$E/public-addresses/backfill?$Q&dryRun=false" \
    -H "Authorization: Bearer $TOKEN" > write.json || break
  diff <(jq -S '[.items[] | {entityId, slug}]' dry.json) \
       <(jq -S '[.items[] | {entityId, slug}]' write.json) || break
  CURSOR=$(jq -r '.nextCursor // empty' write.json)
  [ -z "$CURSOR" ] && break
done
```

For users set `E=users`; the gateway routes the same path to Account. Users have no manual initialize. A conflicting page rolls back; retry its dry-run before writing again. Review transliteration collisions, duplicate names, and `place-…` / `user-…` fallbacks.

## Address lifecycle

- Slugs are lowercase ASCII letters/digits separated by hyphens, length 1–100, with reserved words and GUID-shaped values rejected. Public reads accept uppercase and normalize it; manual rename must already be canonical.
- Names are transliterated using the shared generator. Existing technical tag keys such as `laptop_friendly` produce a public slug such as `laptop-friendly`; clients must use the returned public value.
- Collisions use bounded deterministic suffixes from a SHA-256 digest of the internal ID. New slugs do not publish raw GUID fragments. Previously allocated addresses remain unchanged.
- Display-name edits do not change Shops/catalog addresses. Explicit rename preserves old addresses as aliases; resolution returns the current canonical address and revision.
- Each registry owns its namespace. Canonical address and historical aliases remain reserved. Deletion turns all addresses into ownerless tombstones in the owner transaction, even while disabled.
- Hidden/deleted shops, unpublished zones, inactive tags, and blocked/deleted users do not resolve publicly. Historical references can become null/omitted, with no GUID fallback.
- Username changes have a seven-day cooldown, including while addresses are disabled. An identical username does not reset it. While enabled, a change updates the canonical address and retains the old alias. Returning to an earlier username reuses that user's alias.
- Old profile links reveal the current username. Tombstones and soft-deleted rows can still be correlated by authorized database operators. The implementation does not promise database-level anonymization.

## Verification

On 2026-09-30 all **1,533 ordinary tests** passed across the ten CI test projects and the separately built ModerationService/MediaService test projects. The real HTTP test `PublicAddressGatewaySmokeTests` uses Kestrel and the production YARP routes for Account, Shops and Moderation, synthetic EF data, and an in-process command dispatcher. It verifies read shapes, slug filters, internal endpoint isolation/authentication, moderation/check-in writes, invalid null array elements, profile aliases/privacy and absence of aggregate IDs in public JSON.

The live gate passed on PostgreSQL **17.11** in a separate disposable container authorized by the user: Shops **33/33**, Account **19/19**, errors/failures/skips **0**. The container had a 512 MiB memory limit, a 0.5 CPU limit, a loopback-only port and an SSH tunnel. It used no application database; its container and anonymous volume were removed after the run. Tests cover real EF migrations/model alignment, all eight registries, constraints, restrictive FKs, flag-off deletion retirement, concurrent allocation/rename, aliases/batch filters, backfill, transaction/savepoint rollback and Account cooldown races.

Run ordinary tests with the repository CI solution. On Windows where test apphosts are blocked, build Release and run each test DLL with `dotnet exec … -noColor -notrait Category=LivePostgres`.

To repeat the live gate:

- Use a disposable PostgreSQL 17+ server with UTF8 and `pg_trgm`. A dedicated `LOGIN CREATEDB NOSUPERUSER NOCREATEROLE NOREPLICATION` role is sufficient.
- Set `COFFEEPEEK_LIVE_PG` to a password-free connection string whose database is exactly `postgres`. Supply the password through `PGPASSFILE` outside the repository. A superuser is refused unless `COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER=1`, reserved for disposable servers.
- The harness creates/drops only `cp_live_<service>_<timestamp>_<8hex>` databases, never application databases. It drops each database after its test and sweeps only this role's own leftovers older than six hours. Lock timeout is 30 seconds.
- Run both infrastructure test DLLs with `-trait Category=LivePostgres -showLiveOutput -parallel none`. A pass requires errors/failures/skips all zero. A skipped suite is not evidence.
- Share only counts/version, never credentials or connection details. A production schema-dump rehearsal and an automated CI PostgreSQL service job are not included.

The vulnerability audit still reports the repository's pre-existing `Microsoft.OpenApi` 2.0.0 and `OpenTelemetry.Api` 1.15.0 advisories; this change adds no packages and does not resolve those separate dependency findings.

## Rollback

Disabling a flag returns 503 from address-dependent public APIs. It does **not** restore public GUID routes in this build. If client rollback is needed, roll back the coordinated service binaries and clients while retaining the upgraded schema. Never migrate Down after allocation: dropping registries loses aliases/tombstones, and restoring the old audit kind check fails once catalog-kind rows exist.

Retirement still runs with flags disabled. Test rollback in a disposable environment, including deletion and re-enable, before using it operationally. No production rollout or backfill is performed by the code change itself.
