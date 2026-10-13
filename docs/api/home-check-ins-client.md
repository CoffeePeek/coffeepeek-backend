# Client contract: private home coffee check-ins

Use the gateway base URL. Every route below requires `Authorization: Bearer <user JWT>` and uses the signed-in user as the owner. A home check-in records **one preparation**, including a batch that makes several servings. `doseGrams` is the dry coffee used for the entire brew; `brewDurationSeconds` is the total brew time. There is no serving count, shop, place/service rating, or public visibility field.

The API uses the normal `Response<T>` envelope: `{ "isSuccess": true, "message": "Operation successful", "data": ... }`. Home responses use `Cache-Control: no-store`.

## Routes

| Method | Route | Result |
| --- | --- | --- |
| `POST` | `/api/v1/check-ins/home` | Create a private preparation; `200` with `HomeCheckInDto`. |
| `GET` | `/api/v1/check-ins/home?pageNumber=1&pageSize=20&from=2026-09-01&to=2026-09-30` | Owner history; `200` with `{ items, totalCount }`. Date bounds are inclusive and optional. Page number is clamped to 1–10000, size to 1–100. Pagination headers are `X-Total-Count`, `X-Total-Pages`, `X-Current-Page`, `X-Page-Size`. Entries are ordered by preparation time, newest first. |
| `GET` | `/api/v1/check-ins/home/calendar?from=2026-09-01&to=2026-09-30` | `200` with `{ preparedOn, brewCount }[]`, one item per date that has a brew. Both dates are required, inclusive, ordered, and at most 366 days apart. |
| `GET` | `/api/v1/check-ins/home/{id}` | Owner detail; inaccessible, deleted, shop, and unknown IDs return `404`. |
| `PUT` | `/api/v1/check-ins/home/{id}` | Replace editable fields; `200` with updated detail. Existing photos remain attached. |
| `DELETE` | `/api/v1/check-ins/home/{id}` | Soft delete; `200` success envelope. The entry disappears from history, calendar, and statistics. |
| `GET` | `/api/v1/check-ins/home/stats/beans` | Owner's per-bean brew counts, total known dose, and rated average. |
| `GET` | `/api/v1/check-ins/home/stats/methods` | Same measures grouped by home method code. |

## Create and edit

```http
POST /api/v1/check-ins/home
Authorization: Bearer <user JWT>
Content-Type: application/json

{
  "preparedAt": "2026-09-21T01:15:00+03:00",
  "preparedOn": "2026-09-21",
  "brewMethodCode": "batch_drip",
  "coffeeProductSlug": "honey-bloom",
  "doseGrams": 75,
  "brewDurationSeconds": 360,
  "tasteScore": 4,
  "text": "A batch for friends"
}
```

`preparedAt` is required in `YYYY-MM-DDTHH:mm:ss[.fraction](Z|±HH:mm)` form; seconds and `Z` or an explicit offset such as `+03:00` are mandatory. The server stores its UTC instant in `preparedAtUtc`. `preparedOn` is the calendar date shown in history and calendar; omit it to use the date in `preparedAt`'s supplied offset. The explicit date may differ from the UTC date by at most one day, which supports travel and entries made around midnight. Backdated preparations are supported; future timestamps beyond the normal five-minute clock tolerance are rejected.

Select a published product by `coffeeProductSlug` from the public coffee catalog (`GET /api/coffees`, `GET /api/coffees/{slug}`). Alternatively send a custom `customBeanName`, or omit both when the bean is unknown. Do not send the slug and custom name together. The response stores a stable `coffeeProductId` for catalog selections and a `beanName` snapshot. Catalog rename or archive does not rewrite an existing entry's name. `coffeeProductId` is a product identity, whereas catalog variant `weightGrams` describes a package; neither is the brew dose.

All fields other than `preparedAt` are optional. Home method codes are `espresso`, `pour_over`, `batch_drip`, `moka_pot`, `cezve`, `aeropress`, `french_press`, and `other`. Clients may remember the last selected code for a one-tap UI, but may omit the field. `tasteScore` is a single result score from 1 through 5; absent scores remain `null`. Optional drink selection uses the existing `drinkSlug` and `customDrinkName` rules from [check-ins.md](check-ins.md).

Example `200` response (identifiers and timestamps shown for illustration):

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "id": "11111111-1111-4111-8111-111111111111",
    "preparedAtUtc": "2026-09-20T22:15:00Z",
    "preparedOn": "2026-09-21",
    "createdAtUtc": "2026-10-01T08:00:00Z",
    "text": "A batch for friends",
    "brewMethodCode": "batch_drip",
    "coffeeProductId": "22222222-2222-4222-8222-222222222222",
    "beanName": "Honey Bloom",
    "doseGrams": 75,
    "brewDurationSeconds": 360,
    "tasteScore": 4,
    "drink": null,
    "photos": [],
    "drinkPhoto": null
  },
  "statusCode": null
}
```

`PUT` sends the same editable fields as `POST`, including a required `preparedAt`. It is a replacement: omitted optional note, bean, method, dose, duration, score, and drink values are cleared. It does not replace or remove existing photos. To change a preparation date, send the intended `preparedAt` and `preparedOn` again. To **retain an existing catalog bean and its historical name snapshot**, send the `coffeeProductId` returned by this entry and omit `coffeeProductSlug` and `customBeanName`. This works after the product is renamed or archived. A different ID is rejected; a new published slug selects a new product. Sending the same currently published slug also keeps the entry's original name snapshot. Omit all three bean selectors to clear the bean.

## Photos

Home entries reuse private check-in storage. Request presigned URLs with authenticated `POST /api/Photos/check-in`, upload each file to its returned URL using the signed `Content-Type` and `x-amz-tagging: is_permanent=False` headers, then send the returned `storageKey` in `photos` on creation. Each `photos` item has `fileName`, `contentType`, `storageKey`, and `size`; only the storage key identifies the upload. Media verifies the owner, uploaded bytes, supported raster MIME, and single use. At most five distinct originals can be attached. The existing optional `drinkPhoto` cutout must be a separate PNG upload and requires at least one original; the backend does not make the cutout.

The detail response supplies protected photo URLs such as `/api/v1/check-ins/{id}/photos/{photoId}`. Fetch these with the owner's bearer token. The same route checks ownership on each request and returns `404` to other users for private home content. Photo bytes are streamed with private, no-store caching. A deleted entry's bytes are no longer readable through this route. Photos cannot be added or replaced by `PUT`.

## Statistics and errors

Bean and method statistics return arrays with `brewCount`, `dosedBrewCount`, `totalKnownDoseGrams`, `ratedBrewCount`, and nullable `averageTasteScore`. Bean rows also have `coffeeProductId` and `beanName`; method rows have `brewMethodCode`. Catalog beans group by product identity, custom names group without a product ID, and unknown beans or methods have a `null` bucket. Missing dose is left out of the sum; missing score is left out of the average. Each batch counts once. For example, two brews with doses `18` and `null`, of which only the first is rated `5`, produce `brewCount: 2`, `dosedBrewCount: 1`, `totalKnownDoseGrams: 18`, `ratedBrewCount: 1`, `averageTasteScore: 5`.

| Status | Cause |
| --- | --- |
| `400` | Missing, malformed, offset-free, or future `preparedAt`; invalid `preparedOn` or date range; unknown method; note over 1,000 characters; custom bean name over 250 characters; dose not in 0.01–10000 grams or over two decimal places; duration not in 1–604800 seconds; score outside 1–5; invalid drink or photo input; multiple bean selectors, or a `PUT` product ID different from the entry's current bean. |
| `401` | No valid user JWT. |
| `404` | Product slug is not currently published, or an entry ID is unknown, deleted, belongs to another user, or is a shop check-in. |
| `409` | An upload reservation already belongs to a committed check-in. |

Errors use the project's error envelope and never reveal another owner's diary. Home entries are not published to `/api/v1/check-ins`, `/api/v1/feed`, shop ratings, or moderation.
