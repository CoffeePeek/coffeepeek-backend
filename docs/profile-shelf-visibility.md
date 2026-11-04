# Public profile shelf (opt-in)

Shelf owns this preference and projection in Shops. Account remains the source
of profile addresses and viewer visibility; no Account database reads or writes
were added. Existing `/api/v1/shelf` owner/archive/photo contracts are unchanged.

## Owner preference

Authenticated `GET /api/v1/shelf/profile-visibility` returns the normal envelope
with `data: { "isVisible": false }` when no preference exists. Authenticated PUT
to the same route requires an explicit JSON boolean, including explicit `false`:

```json
{ "isVisible": true }
```

PUT responds with the saved preference envelope. Identity is always taken from
the signed-in caller; request bodies cannot choose an owner. Writes share the
shelf owner's PostgreSQL transaction lock and the Wolverine outbox transaction.
Confirmed account deletion removes the preference idempotently.

## Public read

`GET /api/v1/profiles/{slug}/shelf` resolves the profile through the same
`IProfileAddressLookup` visibility/block guard as the featured collection. An
unavailable/inaccessible profile returns 404; a hidden shelf returns success
with `data: null`. A visible shelf returns:

```json
{
  "isSuccess": true,
  "data": {
    "items": [{
      "id": "11111111-1111-4111-8111-111111111111",
      "name": "Coffee",
      "roasterName": "Roaster",
      "coffeeSlug": "published-coffee",
      "photoUrl": null
    }],
    "totalCount": 1
  }
}
```

Only active OnShelf bags are projected, in current shelf order. Photo-only bags
have an empty string name; clients use their localized placeholder. Unknown or
unpublished catalog slugs and absent/unsupported photos are null. No owner IDs,
archive entries, rating/verdict, weight/date, private storage keys or owner-only
URLs are serialized. The opt-in is checked again after catalog lookup.
The controller then resolves the profile again and requires the same author ID;
revoked access or a reassigned slug cannot deliver the previous author's cards.

Public photo URLs have the form
`/api/v1/profiles/{slug}/shelf/bags/{bagId}/photos/{photoId}`. Each read resolves
profile visibility, checks the flag and current active bag/photo, then internally
downloads Media bytes as the owner. The response is buffered under a five-second,
1 MiB bound matching the existing private-card validation. Profile and bag/photo
access and the original author identity are checked again after Media responds.
Only matching JPEG/PNG/WebP MIME types are delivered. Responses use `no-store`,
`nosniff`; no redirects, 304 or
public cache is used. Caller cancellation propagates; unavailable/oversized or
invalid Media responses fail before writing public bytes.

## Migration and verification

`20261104065015_AddShelfProfileVisibility` adds only the independent
`ShelfProfilePreferences` table with default `IsVisible=false`. Existing shelves
are never published automatically. Rollback removes the preference and retains
all shelf bags; upgrading again starts with hidden shelves. The dedicated
`ShelfProfileVisibilityMigrationLiveTests` case exercises populated upgrade,
opt-in persistence and rollback/re-upgrade with real PostgreSQL. CI includes it
in the existing required live suite loop (`-failSkips` plus nonzero TRX checks).

Local verification on 2026-11-04: backend build passed (existing/new XML
documentation warnings remain); HTTP/auth/projection/photo regressions 114 passed,
persistence regressions 17 passed, production generated-handler checks 2 passed.
The new live PostgreSQL migration case compiled but was skipped locally because
`COFFEEPEEK_LIVE_PG` was not configured. That skip is not DDL acceptance; the
required CI gate remains pending. EF reported no pending model changes, and
scoped forward/rollback SQL generation passed without applying either script.
No live account or database operation was used. Regenerated Wolverine
adapters include the visibility writer and account-deletion preference cleanup.
Initial generator runs failed on missing local-only RabbitMQ/Redis port values;
their ignored logs remain in `artifacts/profile-shelf`. No production service or
database changes are made by this implementation workflow. Unrelated proven
whitespace-only generator output was excluded; generated formatting was cleaned
without changing handler behavior.

Final independent review identified an access/author race not covered by the
initial 114 passing HTTP cases: profile visibility or slug ownership could change
while catalog/Media work was awaited. The controller now re-resolves access and
binds the result to the same author before returning JSON or photo bytes. Two
delayed HTTP regressions cover revoked access and a different author, and a
photo regression covers slug reassignment. The focused rerun passed 117 cases,
zero failures/skips (`host-identity-rerun.log` and its TRX). Together with the
unchanged 17 persistence and 2 generated-handler checks, 136 non-live cases are
verified. The one PostgreSQL migration case remains locally skipped; no new
live-database acceptance is claimed.
