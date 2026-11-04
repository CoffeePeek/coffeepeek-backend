# Saved collections

Discovery owns the mutable library in separate `SavedCollections`,
`SavedCollectionItems`, `SavedCollectionPreferences` and `SavedCollectionCoverUses`
tables. Existing `/favorite-collections` POST/list/
revoke flows remain immutable export snapshots and retain their links. Library
DTOs have `mode: "list"`; historical DTOs remain `mode: "snapshot"`. The historical
`FavoriteCollections`/`FavoriteCollectionItems` tables, their primary keys and
rows are unchanged. A pre-feature binary cannot select private library rows,
including during mixed-version rollout or rollback. Owner favorites include
legacy rows and shop/roaster membership in any active owned list. A private
legacy heart remains private if the owner cancels the collection picker.

## HTTP contract

All JSON responses use the existing response envelope and `Cache-Control:
no-store`. Owner routes use the authenticated identity, never a body owner ID.

| Method | Route under `/api/v1` | Result/access |
| --- | --- | --- |
| GET | `/saved-collections?kind=roaster&slug=sample` | Owner summaries; optional target adds `containsItem` in one read |
| POST | `/saved-collections` | Owner creates an empty or populated library list, 201 |
| PUT | `/saved-collections/default` | Idempotently returns or creates the owner's canonical «Избранное», 200 |
| GET | `/saved-collections/{id}` | Owner detail, including private lists; foreign/missing/snapshot IDs are 404 |
| PUT | `/saved-collections/{id}` | Owner replaces editable metadata with `expectedRevision` |
| PUT | `/saved-collections/{id}/items` | Owner replaces ordered items with `expectedRevision`; empty is valid |
| PUT | `/saved-collections/membership/{kind}/{slug}` | Atomically applies owner picker changes; optional deltas preserve untouched concurrent memberships |
| DELETE | `/saved-collections/{id}` | Owner removes a library list; repeated owner deletion is idempotent |
| GET/PUT | `/saved-collections/featured` | Owner preference `{ "collectionId": null }` or one owned public library ID |
| GET | `/profiles/{authorSlug}/collection` | Anonymous featured public detail or `data: null`; unavailable profile is 404 |
| GET | `/favorite-collections/{id}` | Existing anonymous public snapshot read, also supports public library lists |
| GET | `/favorite-collections/{id}/cover/{photoId}` | Anonymous current cover of an active public library only |
| GET | `/saved-collections/{id}/cover/{photoId}` | Owner-only current cover, including private lists |

Summary fields: `id`, `mode`, `title`, `description`, `visibility`, `coverPhoto`,
`itemCount`, `revision`, `containsItem`, `isFeatured`, `isDefault`, `canonicalPath`. Detail adds
`createdAtUtc`, `updatedAtUtc`, and the existing public shop/roaster `items`.
`coverPhoto` is nullable `{ "id": "...", "url": "/api/v1/..." }`. Resolve its
server-relative URL against the API origin. Private URLs require authentication;
storage keys and arbitrary external cover URLs never appear in read DTOs.
`canonicalPath` is null for private library lists and `/collections/{id:N}` for
public ones. `isDefault` is a server-owned identity marker and persists when its
list is renamed or made public. The first ensure adopts the oldest owned active
list titled exactly «Избранное», without changing its visibility or merging other
same-title lists; otherwise it creates a private empty list. An older client POST
with the exact empty private «Избранное» shape is also treated idempotently, so
it cannot recreate the default after publication. On first private ensure only,
available legacy shop/roaster hearts absent from every owned list
are added to the default. Existing public defaults are never filled from private
legacy hearts. Drinks stay in legacy favorites and cannot be collection members.
Public common-read DTOs expose `itemCount` from the actually returned
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

Metadata PUT requires an explicit `visibility`; omitting it returns 400
`INVALID_COLLECTION` and cannot silently privatize a public list.

Title is trimmed, 1–100 characters, without controls. Description is nullable,
at most 1000 characters, and permits newlines/tabs. Visibility is `private` or
`public`; new library lists default to private. Private means owner-only, including
their covers: anonymous URLs return 404 even if the list used to be public or
featured. Privatizing/deleting a list clears its featured preference atomically.
The default preference is none. Selecting another list replaces the first;
foreign/private/snapshot IDs cannot be featured.

Items PUT body is `{ "items": [{ "kind": "roaster", "slug": "sample" }],
"expectedRevision": 2 }`. Allowed kinds are `coffee_shop` and `roaster`.
New members must be available public shops or roasters. A legacy unheart removes
the target from every active owned list; slug changes retain stable entity IDs and
mutable positions preserve order. All shops precede roasters in public projection,
preserving order within each group. The prior 100-item selection cap is absent.
Availability checks are batched, and duplicate/existing
membership checks use sets. Public-address resolution retains per-slug alias handling.
The existing separate 100-active-links quota remains for snapshots; library lists
have their own 100-active-list creation guard.

Picker PUT accepts `{ "collectionIds": ["11111111-1111-1111-1111-111111111111"],
"addedCollectionIds": [], "removedCollectionIds": [] }`. When both delta arrays
are supplied, the server applies them to current membership under the owner lock;
`collectionIds` remains a compatibility fallback for older servers and clients.
With no deltas, an empty `collectionIds` removes the target from all owned lists. Foreign, deleted
or snapshot IDs reject the entire request. Repeating the same membership set is
idempotent, including revisions. Active collection membership synchronizes the
legacy favorite projection; clearing the target from all lists removes its legacy
favorite. Heart PUT adds a shop/roaster to the canonical default only while that
list is private. If it is public, the heart remains private until an explicit picker
selection; no private heart is implicitly published. Heart DELETE removes the target
from all active owned lists. These operations never change immutable snapshot membership.
Deletion revokes all access immediately; revoked list metadata
and memberships remain in Shops until account deletion, which physically removes
them. Retaining the ID and cover-use ledger prevents delayed requests from reusing
a released upload. Repeated deletion remains idempotent. Owner writes serialize through a transaction-scoped lock;
metadata/items version mismatch returns 409 `COLLECTION_CONFLICT`. Invalid input
uses 400 `INVALID_COLLECTION`; unavailable targets use 409
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
responses disable caching and content sniffing. Shops buffers each Media cover
response within a five-second deadline and a 1 MiB bound before sending public
bytes. Media connection failures, timeouts and invalid oversized responses return
503; client-request cancellation is propagated.

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
