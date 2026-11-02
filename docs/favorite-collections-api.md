# Favorite collections

Discovery owns `FavoriteCollections` and `FavoriteCollectionItems`. A creator
explicitly publishes selected favorite shops/roasters. Membership uses stable
entity GUIDs and positions, so later unfavoriting or slug changes do not change
the selected membership. Public reads project current names, photos and canonical
addresses; hidden, closed or deleted businesses are omitted. No author identity,
private favorite list, or account credential is returned.

| Method | `/api/v1/favorite-collections` suffix | Access | Response |
| --- | --- | --- | --- |
| POST | none | Signed-in owner | 201, response envelope containing the collection |
| GET | none | Signed-in owner | 200, envelope containing active owned summaries |
| GET | `/{id:guid}` | Anonymous | 200, envelope containing published cards; 404 if absent/revoked |
| DELETE | `/{id:guid}` | Owner only | 204, including repeated owner revocation; 404 for absent/foreign IDs |

All responses use `Cache-Control: no-store`. Public reads use the existing global
gateway rate limiter. Only the exact GET-by-GUID gateway route is anonymous;
list/create/delete require authentication. Mutations run through Wolverine and
its EF transaction. Confirmed account deletion removes that owner's collections
and item rows; repeating the account-deletion event is safe.

## Create

```json
{
  "title": "Мои любимые места",
  "items": [
    { "kind": "coffee_shop", "slug": "my-shop" },
    { "kind": "roaster", "slug": "my-roaster" }
  ]
}
```

Title: trimmed, 1–100 characters, no controls. Selection: 1–100 distinct
`coffee_shop`/`roaster` targets, all currently in the caller's favorites and
publicly available. Public-address aliases resolve before deduplication; the
client cannot select another account's private membership. Invalid selections
return the existing Discovery 400 contract (`INVALID_COLLECTION`); changed or
unavailable membership returns 409 (`COLLECTION_CHANGED`). A soft quota check
rejects creation after 100 active owned links (`COLLECTION_LIMIT`, 409); this is
not a hard concurrency quota.

Collection fields are `id`, `title`, `canonicalPath`, `createdAtUtc`, `items`.
The link is `https://coffeepeek.by/collections/{id:N}` (lowercase 32-digit GUID).
Cards extend the existing Discovery favorite projection with nullable `cityName`
and `streetAddress`. Summaries contain `id`, `title`, `canonicalPath`,
`createdAtUtc`, and original membership `itemCount`. Public `items` can be shorter
than that count when targets become unavailable. The owner can revoke the link;
downloaded images and copies already delivered to recipients cannot be recalled.

## Delivery and verification

Apply migration `20261102130458_AddFavoriteCollections` before starting the new
Shops host. It only adds two tables, indexes, an item-kind check and the collection
FK; existing favorites are neither migrated nor rewritten. Roll back the host
before removing these tables, and retain rows if a later roll-forward is planned.

Coordinate the anonymous website route `/collections/:id` and associated-domain
files with the mobile release. Existing installed apps without this route are a
release acceptance gate; verify opening from a browser and a messenger on Android
and iOS. Deploying this PR alone does not certify the whole sharing flow.

Local checks on 2026-11-02 cover EF InMemory persistence contracts, original
favorites regressions, account deletion, owner isolation, unavailable targets,
Gateway routing, controller anonymity, architecture ownership and static
Wolverine registration. Generated handlers were produced with the standard
`codegen write`; generator-only whitespace churn was excluded. Model consistency,
builds and executed suite results are recorded in the PR. No production database
or real-account write was used. PostgreSQL migration/transaction acceptance and
anonymous gateway-to-website reads remain deployment gates.
