# Client: my moderation submissions

All four `/mine` endpoints return only the signed-in caller's submissions, newest first. Caller identity comes from the JWT; a query cannot select another user's submissions. Anonymous shop/roaster suggestions have no owner and never appear in `/mine`.

The current API uses public slugs. See the [client contract](public-addresses-client.md) for submission bodies and shared types.

## Requests

Use `Authorization: Bearer {jwt}`. Missing/invalid authentication returns 401; no moderator role is required.

| GET | Item type |
| --- | --- |
| `/api/ModerationShops/mine` | `PublicModerationShopDto` |
| `/api/ModerationRoasters/mine` | `PublicModerationRoasterDto` |
| `/api/ModerationReviews/mine` | `PublicModerationReviewDto` |
| `/api/ShopChangeRequests/mine` | `PublicShopChangeRequestDto` |

Shared query parameters: `page=1`, `pageSize=20` (1–100), optional `status=Pending|Approved|Rejected`. Shops, roasters and reviews validate pagination; change requests use the existing handler's clamping. Change requests additionally accept `shop={slug}` and `section=Photos|Contacts|Description|Tags|Roasters|Equipment|Menu|BrewMethods`.

## Response

Every list uses the same shape; read `data.items`, not `moderationShops`, `reviewDtos` or a second address endpoint:

```json
{
  "isSuccess":true,
  "data":{
    "items":[{
      "id":"c2d9e3f4-1111-4c5b-9e1a-7a6b5c4d3e2f",
      "name":"26",
      "moderationStatus":"Approved",
      "rejectedReason":null,
      "city":{"slug":"minsk","canonicalPath":"/cities/minsk","revision":1,"isAlias":false},
      "publishedShop":{"slug":"26-october-16","canonicalPath":"/coffee-shops/26-october-16","revision":1,"isAlias":false},
      "beans":["arabica"],"equipments":[],"roasters":[],"brewMethods":[]
    }],
    "totalItems":1,"totalPages":1,"currentPage":1,"pageSize":20
  }
}
```

The item `id` is a service workflow ID, not the published shop/roaster ID. It remains a GUID for tracking and editing the submission. Public aggregate GUIDs, submitter/reviewer GUIDs and photo owner GUIDs are excluded.

- Shops: `city` and `publishedShop` are addresses or null. `publishedShop` can remain null briefly after approval while the Shops consumer publishes it. Catalog references are `beans`, `equipments`, `roasters`, `brewMethods`, arrays of slug.
- Roasters: `city` is an address or null; status/rejection fields and photos are preserved.
- Reviews: `shop` is an address or null. Rating, header, comment, username, timestamps and photos are preserved.
- Change requests: `shop` is an address or null. Payload uses `tags`, `roasters`, `equipments`, `brewMethods` as slug arrays; photo IDs remain GUIDs. Status/timestamps/rejection reason are preserved.

Hidden/deleted historical references become null or are omitted from reference arrays, with no GUID fallback. The usual pagination headers are also present: `X-Total-Count`, `X-Total-Pages`, `X-Current-Page`, `X-Page-Size`. Responses use no-store.

A regular user's `GET /api/ShopChangeRequests/{workflowId}` and successful POST also return the public item shape. Moderator-only query/update endpoints retain administrative DTOs and internal IDs.
