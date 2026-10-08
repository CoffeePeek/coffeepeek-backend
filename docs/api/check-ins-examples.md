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
  "text": "A good visit.",
  "rating": { "coffee": 5, "service": 4, "place": 4 }
}
```

Use the existing shop address slug from `data.coffeeShops[].address.slug` in `GET /api/CoffeeShops`, or `data.shopDto.address.slug` in `GET /api/CoffeeShops/{slug}`. The public boundary resolves that slug through the existing registry; callers do not need an aggregate UUID. Slug aliases use the existing registry behavior. Omitted visibility is Private; omitted visit time is now in UTC.

To select a catalog drink, send its `slug` as `drinkSlug`, for example `"drinkSlug": "cappuccino"`. `GET /api/menu/drinks` exposes `slug`, `nameRu`, `nameEn`, `category` and `sortOrder`; the existing consumed-drink catalog at `GET /api/catalogs/drinks` also includes the Other choice. Send `"drinkSlug": "other", "customDrinkName": "Espresso tonic"` for a custom drink. Other requires a nonblank name of at most 100 characters; named catalog selections reject a custom name. Frozen drink names survive later catalog renames/deactivation. Omitting both drink fields is valid.

Public creation adds `"visibility": "Public"`; text and all three scores stay mandatory. Optional photos are uploaded first using `POST /api/Photos/check-in`; submit their upload metadata in `photos`. Optional `visitedAt` must not be in the future. The complete pending response illustrates a catalog selection and one attached photo.

The public visit has flat `drinkSlug`, `customDrinkName`, `drinkNameRu`, `drinkNameEn` fields and `shop`/`author` public addresses. Use its operational `id` for read/edit/visibility/helpful/report routes; `photos[].id` selects protected bytes. Aggregate shop/author/drink UUIDs are absent.

## Pagination

The owner example has `data.items` and `data.totalCount: 3` with headers `X-Total-Count: 3`, `X-Total-Pages: 1`, `X-Current-Page: 1`, `X-Page-Size: 10`. The public feed uses opaque `data.nextCursor`; send a non-null value as `cursor` on the next request. Filter with `coffeeShopSlug`, `citySlug` or `authorSlug`.

See [check-ins.md](check-ins.md) for lifecycle/privacy/ratings/moderation. These examples describe the integrated source contract; no deployed API or live data was queried.
