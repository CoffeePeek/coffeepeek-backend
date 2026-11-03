# Saved collections

Discovery owns the mutable library in separate `SavedCollections`,
`SavedCollectionItems`, `SavedCollectionPreferences` and `SavedCollectionCoverUses`
tables. Existing `/favorite-collections` POST/list/
revoke flows remain immutable export snapshots and retain their links. Library
DTOs have `mode: "list"`; historical DTOs remain `mode: "snapshot"`. The historical
`FavoriteCollections`/`FavoriteCollectionItems` tables, their primary keys and
rows are unchanged. A pre-feature binary cannot select private library rows,
including during mixed-version rollout or rollback. Server
favorites remain independent. Saving a global favorite opens the client picker;
cancelling that picker does not undo the favorite.

## HTTP contract

All JSON responses use the existing response envelope and `Cache-Control:
no-store`. Owner routes use the authenticated identity, never a body owner ID.

| Method | Route under `/api/v1` | Result/access |
| --- | --- | --- |
| GET | `/saved-collections?kind=roaster&slug=sample` | Owner summaries; optional target adds `containsItem` in one read |
| POST | `/saved-collections` | Owner creates an empty or populated library list, 201 |
| GET | `/saved-collections/{id}` | Owner detail, including private lists; foreign/missing/snapshot IDs are 404 |
| PUT | `/saved-collections/{id}` | Owner replaces editable metadata with `expectedRevision` |
| PUT | `/saved-collections/{id}/items` | Owner replaces ordered items with `expectedRevision`; empty is valid |
| PUT | `/saved-collections/membership/{kind}/{slug}` | Atomically sets the target's owned library memberships |
| DELETE | `/saved-collections/{id}` | Owner removes a library list; repeated owner deletion is idempotent |
| GET/PUT | `/saved-collections/featured` | Owner preference `{ "collectionId": null }` or one owned public library ID |
| GET | `/profiles/{authorSlug}/collection` | Anonymous featured public detail or `data: null`; unavailable profile is 404 |
| GET | `/favorite-collections/{id}` | Existing anonymous public snapshot read, also supports public library lists |
| GET | `/favorite-collections/{id}/cover/{photoId}` | Anonymous current cover of an active public library only |
| GET | `/saved-collections/{id}/cover/{photoId}` | Owner-only current cover, including private lists |

Summary fields: `id`, `mode`, `title`, `description`, `visibility`, `coverPhoto`,
`itemCount`, `revision`, `containsItem`, `isFeatured`, `canonicalPath`. Detail adds
`createdAtUtc`, `updatedAtUtc`, and the existing public shop/roaster `items`.
`coverPhoto` is nullable `{ "id": "...", "url": "/api/v1/..." }`. Resolve its
server-relative URL against the API origin. Private URLs require authentication;
storage keys and arbitrary external cover URLs never appear in read DTOs.
`canonicalPath` is null for private library lists and `/collections/{id:N}` for
public ones. Public common-read DTOs expose `itemCount` from the actually returned
`items` array, including when unavailable targets have been omitted. The public
featured-profile detail uses the same count rule. Public snapshot DTOs gain optional/defaulted `mode`, `visibility`,
`description`, `coverPhoto`, `revision`, and `updatedAtUtc` without losing fields.

Create body:

```json
{ "title": "Weekend coffee", "description": "A few places to visit", "visibility": "private", "items": [] }
```

Metadata PUT body:

```json
{ "title": "Weekend coffee", "description": null, "visibility": "public", "expectedRevision": 1, "removeCover": false }
```

Title is trimmed, 1–100 characters, without controls. Description is nullable,
at most 1000 characters, and permits newlines/tabs. Visibility is `private` or
`public`; new library lists default to private. Private means owner-only, including
their covers: anonymous URLs return 404 even if the list used to be public or
featured. Privatizing/deleting a list clears its featured preference atomically.
The default preference is none. Selecting another list replaces the first;
foreign/private/snapshot IDs cannot be featured.

Items PUT body is `{ "items": [{ "kind": "roaster", "slug": "sample" }],
"expectedRevision": 2 }`. Allowed kinds are `coffee_shop` and `roaster`.
New members must already be the owner's available server favorites. Existing
membership survives later unfavoriting and slug changes; stable entity IDs and
mutable positions preserve order. All shops precede roasters in public projection,
preserving order within each group. The prior 100-item selection cap is absent.
The existing separate 100-active-links quota remains for snapshots; library lists
have their own 100-active-list creation guard.

Picker PUT body is `{ "collectionIds": ["11111111-1111-1111-1111-111111111111"] }`.
An empty array removes the target from all owned library lists. Foreign, deleted
or snapshot IDs reject the entire request. Repeating the same membership set is
idempotent, including revisions. This operation never writes global favorites or
snapshot membership. Owner writes serialize through a transaction-scoped lock;
metadata/items version mismatch returns 409 `COLLECTION_CONFLICT`. Invalid input
uses 400 `INVALID_COLLECTION`; unavailable/new non-favorite targets use 409
`COLLECTION_CHANGED`.

## Cover upload and lifecycle

Only Save starts uploading. The client can create an empty list first, retain its
ID across retries, then upload/edit it and replace its items; retry must not create
another list. Presign uses the existing Media response envelope:

```text
POST /api/Photos/collection-cover
Content-Type: application/json

[{"fileName":"cover.jpg","contentType":"image/jpeg","sizeBytes":102400}]
```

Exactly one JPEG/PNG/WebP of at most 1 MiB is accepted. Presign response data is
`[{photoId,uploadUrl,storageKey}]`. PUT the image bytes to `uploadUrl`, preserving `Content-Type`
and signed `x-amz-tagging: is_permanent=False`. Metadata Save passes
`coverUpload: {fileName,contentType,storageKey,size}`. Only the storage key is
trusted; Media verifies ownership, purpose, actual bytes, raster decoding and a
maximum dimension of 1280 pixels. An omitted/null upload keeps the current cover;
`removeCover: true` clears it. Replacement and removal cannot be combined.

Uploads and immutable sealed copies use the private check-in bucket under the
dedicated `collection-covers/` prefix. They cannot be claimed by a check-in,
shelf bag, ownership-evidence flow or published shop photo. Claim reserves one
photo for one collection. A successful metadata transaction emits a durable
attachment event; only that event makes the sealed cover permanent. A lost or
rejected Save leaves an expiring temporary upload and never replaces the current
cover. Replacement/removal/account deletion emits idempotent release events.
A Discovery cover-use ledger prevents a replaced upload being reused while its
old release is in flight. Re-uploading the same image yields a fresh identity.

An hourly bounded cleanup reconciles up to 100 cover uploads abandoned for at least
24 hours since their last claim, or since upload when never claimed. The claim
time is rechecked under the photo lock, allowing an in-flight Shops save to commit.
Unclaimed/abandoned objects are deleted, including their original upload keys.
For a validated claim it asks Shops whether the photo is still current: current
covers are confirmed, unreferenced ones are deleted, and unavailable authority
retains the object for retry. These service-to-service endpoints require signed
gateway identity headers and have no public Gateway routes. Public cover streams
check current list visibility, owner association and exact photo ID on every read;
responses disable caching and content sniffing.

## Privacy audit and verification

Global favorites now return an empty array to guests and other profile viewers at
the legacy `/profiles/{slug}/favorites` route, without reading the favorite store.
The owner retains that route and `/favorites`. Public profile collections use the
separate opt-in endpoint above. Account profile DTOs contain no favorite counts or
IDs; profile activity queries read public visits only. Discovery favorite writers
publish no feed/notification event, and public feed/search projections do not join
another user's favorite/list memberships. Existing public check-in privacy remains
unchanged. Media has no generic public photo-by-ID/metadata/download endpoint;
its public controller exposes owner-bound presign/resolve writes, and protected
read controllers stay internal.

Verification plan: new private/owner/metadata/membership/featured/cover/cleanup
contracts; existing snapshot and global-favorite regressions; actual HTTP auth and
error contracts; Gateway routes; ownership/host boundaries; EF model and generated
Shops/Media migrations; production Wolverine registry/startup checks. No production
database/account writes or deployment are part of these checks. In-memory tests
do not certify PostgreSQL locking/migration execution, real MinIO policy/sealing,
RabbitMQ delivery, or deployment; those remain separate integration gates.

## Verification record — 2026-11-03

Working patch on `31efbde7`, branch `codex/saved-collections`:

- Release solution build passed. Unit/contract runs passed: Shops persistence 28,
  Media 244 (live classes excluded), Shops HTTP/ownership/registry 107, Gateway
  routes 34: 413 passed, zero failures/skips within those executed selections.
  Cross-contract review added a public two-card HTTP serialization assertion for
  `itemCount: 2` and a zero-count check when a legacy target becomes unavailable;
  the affected 107 HTTP/ownership/registry and 28 persistence cases passed again.
  Media's existing `photoId`/`uploadUrl` reservation response is retained.
- Both EF pending-model checks returned no differences. Generated upgrade and
  idempotent SQL were inspected without connecting to a database. Migrations are
  `20261103135540_AddSavedCollectionLibrary` (four new Saved tables only) and
  `20261103134700_AddCollectionCovers` (two nullable Media columns and an index).
  Both downgrade bodies refuse to discard new populated library/binding data;
  the historical snapshot schema and PK are untouched.
- Production Wolverine adapters/registries were generated with the normal CLI.
  Only new commands/consumers, their registries and account-deletion integration
  remain in the diff; unrelated generator order/format churn was excluded. The
  actual registry and production-startup suites passed in the selections above.
- A first compile found a missing address-enum import; a later test edit placed
  an assertion in the wrong method. Both were corrected before the successful
  runs. Original failure logs remain local, separate from successful reruns.
- This Windows host has no running Docker Linux engine. PostgreSQL DDL and owner
  lock tests were compiled but not executed locally: the two Shops live cases
  and one Media live case report skipped without `COFFEEPEEK_LIVE_PG`.
- CI explicitly runs
  `CoffeePeek.Shops.Infrastructure.Tests.LivePostgres.SavedCollectionsMigrationLiveTests`
  and `CoffeePeek.MediaService.Tests.CollectionCoverMigrationLiveTests` against
  its disposable PostgreSQL 17 service. Each uses `-failSkips` and nonzero TRX
  executed/passed checks. The cases preserve populated legacy rows/PK/order,
  prove physical private-row isolation, exercise guarded downgrade and verify
  real advisory-lock/stale-version behavior. Passing CI evidence is still needed.
- History and staged-addition secret scans reported no leaks. No commit, PR,
  production deployment, real-account write, or existing user-data migration was
  performed during these local checks. SQL/log/capture artifacts stay ignored.
