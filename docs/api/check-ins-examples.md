# Complete check-in responses

These four files contain the complete responses serialized by the service's actual MVC configuration and existing PublicDiscoveryService projection. Slugs, visit/photo UUIDs and timestamps are synthetic examples, not deployed records. Catalog entries come from the 14 existing seed rows. Related shops and authors use public addresses; only visit/photo workflow resources have UUID identifiers.

| Request | Full response |
| --- | --- |
| `POST /api/v1/check-ins` | [check-in.json](examples/check-in.json): newly submitted Public/Pending visit |
| `GET /api/v1/check-ins/mine?pageNumber=1&pageSize=10` | [check-ins-mine.json](examples/check-ins-mine.json): author history with public pending, private and public approved visits |
| `GET /api/v1/check-ins?pageSize=20` | [check-ins-feed.json](examples/check-ins-feed.json): current approved public visit; null cursor marks the last page |
| `GET /api/menu/drinks` | [menu-drinks.json](examples/menu-drinks.json): all 14 seeded drinks, identified by slug |

Every emitted property, null envelope field and named enum is included. Creation and owner history require a Bearer token. The public pending photo becomes readable by other readers only after approval of the current public revision; its protected URL rechecks access on every request.

## Creating a visit

Minimal `POST /api/v1/check-ins` body:

```json
{
  "coffeeShopSlug": "example-coffee",
  "visitedAt": "2026-10-01T09:00:00Z"
}
```

Use the existing shop address slug from `data.coffeeShops[].address.slug` in `GET /api/CoffeeShops`, or `data.shopDto.address.slug` in `GET /api/CoffeeShops/{slug}`. The public boundary resolves that slug through the existing registry; callers do not need an aggregate UUID. Slug aliases use the existing registry behavior. Omitted visibility is Private. Visit time is required and normalized to UTC. Text and rating can be omitted; the response then contains `"text": ""` and `"rating": null`.

To select a catalog drink, send its `slug` as `drinkSlug`, for example `"drinkSlug": "cappuccino"`. `GET /api/menu/drinks` exposes `slug`, `nameRu`, `nameEn`, `category` and `sortOrder`; the existing consumed-drink catalog at `GET /api/catalogs/drinks` also includes the Other choice. Send `"drinkSlug": "other", "customDrinkName": "Espresso tonic"` for a custom drink. Other requires a nonblank name of at most 100 characters; named catalog selections reject a custom name. Frozen drink names survive later catalog renames/deactivation. Omitting both drink fields is valid.

Public creation adds `"visibility": "Public"`; text and rating remain optional. If rating is supplied, all three scores must be integers from 1 through 5. Optional photos are uploaded first using `POST /api/Photos/check-in`; submit their upload metadata in `photos`. Required `visitedAt` must not be in the future (the existing clock-skew tolerance applies). The complete pending response illustrates optional text, scores, a catalog selection and one attached photo.

The public visit has flat `drinkSlug`, `customDrinkName`, `drinkNameRu`, `drinkNameEn` fields and `shop`/`author` public addresses. Use its operational `id` for read/edit/visibility/helpful/report routes; `photos[].id` selects protected bytes. Aggregate shop/author/drink UUIDs are absent.

## Transparent drink cutout

Upload the original photos and one cutout through `POST /api/Photos/check-in`, then PUT
the bytes to their returned upload URLs. Pass the original metadata in `photos` and
the cutout separately in optional `drinkPhoto`:

```json
{
  "coffeeShopSlug": "example-coffee",
  "visitedAt": "2026-10-01T09:00:00Z",
  "photos": [{"fileName":"full.jpg","contentType":"image/jpeg","storageKey":"original-upload-key","size":1200000}],
  "drinkPhoto": {"fileName":"cup.png","contentType":"image/png","storageKey":"cutout-upload-key","size":100000}
}
```

Both uploads must belong to the authenticated author and be unused. The server uses
trusted upload metadata, freezes the bytes under private immutable keys, and decodes
the cutout to verify PNG, dimensions of 1–768 px per side, at most 4 MiB, a visible
object and transparent pixels. An invalid/missing cutout fails creation; the server
never silently creates a visit without the selected cutout. Five original photos
plus one cutout are allowed. Animated PNGs are rejected. PNG bytes are not reencoded.

`drinkPhotoUrl` is returned by create/edit/visibility, owner history (calendar),
shop/user check-in lists, public detail and both feed APIs. It uses the existing
`/api/v1/check-ins/{id}/photos/{photoId}` protected stream. The PNG is excluded from
`photos`, which remains the original gallery. Old records and requests without a
cutout continue to work and return null. Text edits preserve the cutout.

The same current visibility, approval, hidden-shop and soft-deletion checks protect
both image versions. Moderation snapshots contain both, so a separate uploaded PNG
cannot bypass moderation. Cutouts use the existing upload reservation/confirmation
lifecycle; ambiguous failed claims retain their reservations, as original photos do.
Physical cleanup of abandoned reservations is outside this change.

Deploy MediaService first, apply Shops migration `20261012082218_CheckInDrinkPhoto`,
then deploy all ShopsService instances before releasing the mobile client. The
migration only adds nullable `CanonicalCheckIns.DrinkPhotoId`; old rows need no
backfill. Do not roll back that column after cutouts have been written: old code
would treat them as gallery photos. No Media or Moderation schema change is needed.
MediaService includes SkiaSharp and its Linux native assets for bounded PNG decoding.

## Pagination

The owner example has `data.items` and `data.totalCount: 3` with headers `X-Total-Count: 3`, `X-Total-Pages: 1`, `X-Current-Page: 1`, `X-Page-Size: 10`. The public feed uses opaque `data.nextCursor`; send a non-null value as `cursor` on the next request. Filter with `coffeeShopSlug`, `citySlug` or `authorSlug`.

See [check-ins.md](check-ins.md) for lifecycle/privacy/ratings/moderation. These examples describe the integrated source contract; no deployed API or live data was queried.
