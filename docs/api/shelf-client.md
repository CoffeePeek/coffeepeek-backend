# Client contract: coffee shelf

The shelf is a signed-in user's private list of coffee bags at home. A bag is added from a scanned card image, stands on the shelf in a user-chosen order, and moves to the archive when it is finished. Every route requires `Authorization: Bearer <user JWT>` and acts on the caller's own shelf; nobody else can read it, and another owner's bag answers `404`.

Use the gateway base URL. Responses use the normal `Response<T>` envelope (`{ "isSuccess": true, "message": "Operation successful", "data": ... }`) and `Cache-Control: no-store`. Shops owns shelf rows; Media owns the card image bytes.

## Routes

| Method | Route | Result |
| --- | --- | --- |
| `GET` | `/api/v1/shelf` | `200` with `ShelfBagDto[]`: bags on the shelf ordered by `position`, first bag first. |
| `GET` | `/api/v1/shelf/archive?pageNumber=1&pageSize=20&query=` | `200` with `{ items, totalCount }`: finished bags, newest `finishedAtUtc` first. Page number is clamped to 1–10000, size to 1–100. Optional `query` (at most 100 characters) matches `name` or `roasterName` case-insensitively. |
| `POST` | `/api/v1/shelf/bags` | Create a bag at the front of the shelf (`position` 0); `200` with `ShelfBagDto`. |
| `PUT` | `/api/v1/shelf/bags/{id}` | Replace editable fields and optionally the card; `200` with `ShelfBagDto`. Works for shelf and archive bags. |
| `PUT` | `/api/v1/shelf/order` | Reorder the shelf; `200` with the reordered `ShelfBagDto[]`. |
| `POST` | `/api/v1/shelf/bags/{id}/finish` | Move a shelf bag to the archive; `200` with `ShelfBagDto`. |
| `POST` | `/api/v1/shelf/bags/{id}/restore` | Return an archived bag to the shelf; `200` with `ShelfBagDto`. |
| `DELETE` | `/api/v1/shelf/bags/{id}` | Delete a mistaken bag from the shelf or archive; `200` success envelope. |
| `GET` | `/api/v1/shelf/bags/{id}/photos/{photoId}` | Owner-only card bytes (`private, no-store`). |

Card uploads use Media: `POST /api/Photos/shelf-bag` (see [Card image](#card-image)).

## ShelfBagDto

```json
{
  "id": "11111111-1111-4111-8111-111111111111",
  "status": "OnShelf",
  "position": 0,
  "photo": { "id": "33333333-3333-4333-8333-333333333333", "url": "/api/v1/shelf/bags/11111111-1111-4111-8111-111111111111/photos/33333333-3333-4333-8333-333333333333" },
  "coffeeProductId": "22222222-2222-4222-8222-222222222222",
  "coffeeProductSlug": "nut-n-berry",
  "name": "Nut'n'Berry",
  "roasterName": "Sibaristica",
  "weightGrams": 250,
  "grind": "Beans",
  "roastPurpose": "Espresso",
  "roastedOn": "2026-10-12",
  "addedAtUtc": "2026-10-20T08:00:00Z",
  "finishedAtUtc": null,
  "rating": null,
  "buyAgain": null
}
```

- `status` is `OnShelf` or `Finished`. `position` is the zero-based shelf index for `OnShelf` and `null` for `Finished`.
- `photo` is `null` for a bag added without a card. `photo.id` changes when the card is replaced, so clients may cache by URL.
- `coffeeProductId` is a stable catalog product identity; `coffeeProductSlug` is its currently published slug or `null` when the product is no longer published. `name` is a snapshot taken when the product was selected (or the custom name) and is not rewritten by catalog renames.
- `grind` is `Beans`, `Ground` or `null`. `roastPurpose` is `Espresso`, `Filter` or `null` (unknown). `roastedOn` is a calendar date. `rating` is 1–5. `buyAgain` is `true` ("Куплю ещё"), `false` ("Больше не возьму") or `null`.
- Unknown fields are optional for clients; new fields may be added.

## Create and edit

```http
POST /api/v1/shelf/bags
Authorization: Bearer <user JWT>
Content-Type: application/json

{
  "photo": { "fileName": "bag.jpg", "contentType": "image/jpeg", "storageKey": "shelf-bags/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg", "size": 98304 },
  "coffeeProductSlug": "nut-n-berry",
  "roasterName": null,
  "weightGrams": 250,
  "grind": "Beans",
  "roastPurpose": "Espresso",
  "roastedOn": "2026-10-12"
}
```

All fields are optional, but a bag needs a `photo` or a bean (`coffeeProductSlug` or `customName`). The bean selector follows the home check-in rules:

- `coffeeProductSlug` selects a currently published catalog product. The server stores `coffeeProductId` and a `name` snapshot. When `roasterName` is omitted, the product's roaster name is stored.
- `customName` (1–250 characters after trimming) names a bean that is not in the catalog. Do not send it together with `coffeeProductSlug`.
- On `PUT`, send the bag's own `coffeeProductId` (and neither other selector) to keep the current product and its name snapshot, even after the product was renamed or archived. A different ID is rejected. Keeping the product this way does not fill in the catalog roaster: send the bag's current `roasterName` again to keep it, because the `PUT` is a replacement.

`roasterName` is at most 250 characters, `weightGrams` is an integer 1–10000, and `roastedOn` cannot be more than one day in the future. "Today" is the server's UTC date.

`PUT /api/v1/shelf/bags/{id}` sends the same fields. It is a replacement of the details: omitted bean, roaster, weight, grind, roast purpose and roast date are cleared. `photo` is different: send it to replace the card ("Переснять"), omit it to keep the current one. The previous card's bytes stop being readable after the replacement. `PUT` does not change `status`, `position`, `rating` or `buyAgain`. The shelf holds at most 200 bags with `OnShelf` status; creating more returns `409`.

Duplicates are allowed: a second bag of the same product is a separate bag. Clients warn before adding one.

A create with a card gets a bag ID derived from the caller and the upload key. If the response of a successful create is lost, retrying it with the same key returns `409` instead of adding a second bag; reload `GET /api/v1/shelf`. Retrying a `PUT` with the same new card is safe.

## Order

```http
PUT /api/v1/shelf/order
Content-Type: application/json

{ "bagIds": ["…id at position 0…", "…id at position 1…"] }
```

`bagIds` must contain each of the caller's current `OnShelf` bag IDs exactly once. A missing, extra, duplicate or archived ID returns `409` because the client's shelf is stale; the client reloads `GET /api/v1/shelf` and reapplies the move.

## Finish and restore

```http
POST /api/v1/shelf/bags/{id}/finish
Content-Type: application/json

{ "rating": 4, "buyAgain": true }
```

Both fields are optional and the body may be `{}` ("Пропустить"). The bag gets `Finished` status, `finishedAtUtc` and leaves the shelf; the remaining bags close the gap. Finishing an archived bag returns `409`.

```http
POST /api/v1/shelf/bags/{id}/restore
Content-Type: application/json

{ "position": 2 }
```

`position` is optional and is clamped to 0…current shelf size; it defaults to `0`. Restoring clears `finishedAtUtc`, `rating` and `buyAgain`. Clients use restore with the bag's previous position for "Отменить" after finishing. Restoring a shelf bag returns `409`; a full shelf returns `409`.

## Card image

The card is the already processed front of the bag (about 720 px on the long side, about 100 KB). The original photo never leaves the phone.

1. Request a presigned URL:

   ```http
   POST /api/Photos/shelf-bag
   Content-Type: application/json

   [ { "sizeBytes": 98304, "fileName": "bag.jpg", "contentType": "image/jpeg" } ]
   ```

   The `200` response is the same as for check-in photos: `data[0]` has `photoId`, `uploadUrl` and `storageKey` beginning with `shelf-bags/`. Exactly one item is accepted; more or fewer items, a `contentType` other than `image/jpeg`, `image/png` or `image/webp`, or a `sizeBytes` outside 1–1048576 return `400`. The URL expires after 600 seconds, and an upload must be attached to a bag within 24 hours.

2. `PUT` the bytes to `uploadUrl` with the signed `Content-Type` and `x-amz-tagging: is_permanent=False` headers, exactly as for [check-in photos](photos-client.md#2-upload-the-bytes-to-minio).

3. Send the `storageKey` in `photo` on `POST` or `PUT /api/v1/shelf/bags…`.

Media checks that the upload belongs to the caller, is fully uploaded, is unused, and is a JPEG, PNG or WebP raster of at most 1 MiB and at most 1280 px on each side. A key is single use. Read the card only through `photo.url`, which checks ownership on every request; never build a MinIO URL from `storageKey`. Deleting a bag makes its card unreadable.

## Home check-ins from a bag

A home check-in may name the bag it was brewed from with `shelfBagId` (see [home-check-ins-client.md](home-check-ins-client.md)). The check-in copies the bag's bean and roaster as snapshots at that moment; editing, finishing or deleting the bag later does not change existing check-ins. Clients list a bag's brews by `shelfBagId` from the author's home check-ins. The bag itself and its card stay private.

## Errors

| Status | Cause |
| --- | --- |
| `400` | Malformed body; both bean selectors; neither photo nor bean on create; field out of range; invalid photo input; `query` too long. |
| `401` | No valid user JWT. |
| `404` | Unknown, deleted or another owner's bag; unpublished `coffeeProductSlug`. |
| `409` | Stale order, finishing an archived bag, restoring a shelf bag, a full shelf, or an upload already attached elsewhere (including a retried create whose first attempt succeeded). |
| `503` | Media is unavailable while a card is claimed; retry with the same upload. |

Errors use the project's error envelope and never reveal another owner's shelf. Deleting an account deletes its shelf and makes its cards unreadable.

## Mobile Sandbox

The mobile Sandbox (`com.coffeepeek.sandbox`) answers the same routes from an in-memory fake, so client work can proceed before this service is deployed.
