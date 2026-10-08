# Admin / moderator: photos

How photos show up for moderators and admins, what moderation decisions do to them, and which presign endpoint to use for each shop photo surface.

Base URL: gateway host, e.g. `https://api.example.com` or Aspire `http://localhost:5000`.

Client-side upload flow (presign → `PUT` to MinIO → attach): [photos-client.md](photos-client.md).

---

## Auth

| Surface | Header | Policy |
|---------|--------|--------|
| Canonical check-in submissions (read, change status, protected bytes) | `Authorization: Bearer {moderatorJwt}` | Moderator |
| Admin shop gallery / menu | `Authorization: Bearer {adminJwt}` | Admin |
| Owner shop gallery | `Authorization: Bearer {ownerJwt}` | Owner (must own the shop) |
| Presign (`/api/photos/*`) | `Authorization: Bearer {jwt}` | Authenticated; rate limit `media-upload` = 10 / minute per client |

---

## Types

```ts
type ApiResponse<T> = { isSuccess: boolean; message: string; data?: T };

// Canonical moderation snapshot photo (PhotoMetadataDto)
type PhotoMetadataDto = {
  id: string;
  fileName: string;
  contentType: string;
  storageKey: string;
  fullUrl: string | null; // canonical snapshots always supply the protected moderation-photo route
  sizeBytes: number;
  ownerId: string;        // check-in author
  uploadedAt: string;
  sortIndex: number;
};

type ModerationReviewDto = {
  id: string;
  checkInId: string;
  contentRevision: number;
  visitedAtUtc: string;
  drinkSlug: string | null;
  customDrinkName: string | null;
  drinkNameRu: string | null;
  drinkNameEn: string | null;
  text: string;
  userId: string;
  userName: string;
  shopId: string;
  rating: { place: number; service: number; coffee: number };
  rejectedReason: string | null;
  moderatedBy: string | null;
  createdAt: string;
  moderatedAt: string | null;
  moderationStatus: "Pending" | "Approved" | "Rejected"; // serialized as string
  photos: PhotoMetadataDto[];
};

type UploadedPhotoDto = { fileName: string; contentType: string; storageKey: string; size: number };
```

---

## 1. Check-in photos in moderation

Public check-ins submit immutable canonical snapshots through `CheckInModerationRequestedEvent`. Every snapshot identifies its check-in and content revision. Shops verifies attached photos through Media's trusted claim flow before submission (see [check-ins.md](check-ins.md)). As a result:

- every photo was presigned by the check-in author and actually uploaded to private storage;
- `fileName` / `contentType` are the presign-time values stored in Media, and `sizeBytes` is the real object size;
- no photo is shared with another check-in.

Authors presign new check-in photos through `POST /api/Photos/check-in`; it issues PUT URLs for the private check-in bucket. Supported raster MIME types are JPEG, PNG, GIF, WebP, BMP and AVIF. Moderation decisions operate on the verified immutable object; an upload URL cannot overwrite an already attached photo.

### List

```http
GET /api/v1/moderation/check-ins?page=1&pageSize=20&status=Pending
Authorization: Bearer {moderatorJwt}
```

**200** — `ApiResponse<GetAllModerationReviewsResponse>` with data fields `items`, `totalItems`, `totalPages`, `currentPage`, `pageSize` and pagination headers `X-Total-Count`, `X-Total-Pages`, `X-Current-Page`, `X-Page-Size`.

### Detail

```http
GET /api/v1/moderation/check-ins/{id}
Authorization: Bearer {moderatorJwt}
```

**200** — `ApiResponse<ModerationReviewDto>`; **404** if not found.

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "id": "11111111-1111-1111-1111-111111111111",
    "checkInId": "22222222-2222-2222-2222-222222222222",
    "contentRevision": 1,
    "visitedAtUtc": "2026-09-24T09:31:02Z",
    "drinkSlug": null,
    "customDrinkName": null,
    "drinkNameRu": null,
    "drinkNameEn": null,
    "text": "Friendly baristas and a very balanced espresso.",
    "userName": "anna",
    "moderationStatus": "Pending",
    "photos": [
      {
        "id": "33333333-3333-3333-3333-333333333333",
        "fileName": "latte.jpg",
        "contentType": "image/jpeg",
        "storageKey": "check-ins/33333333333333333333333333333333.jpg",
        "fullUrl": "/api/v1/check-ins/22222222-2222-2222-2222-222222222222/moderation-photos/33333333-3333-3333-3333-333333333333?revision=1",
        "sizeBytes": 734003,
        "ownerId": "…author id…",
        "uploadedAt": "2026-09-24T09:31:02Z",
        "sortIndex": 0
      }
    ]
  }
}
```

Fetch `fullUrl` from the gateway with the moderator bearer token and render the returned bytes. The route requires the current public, non-deleted submission revision and streams from private storage with `Cache-Control: private, no-store` and `X-Content-Type-Options: nosniff`. It returns 404 for hidden/deleted/stale revisions. Never construct a public bucket URL from `storageKey`.

The supported moderation API provides queue/detail GET actions and status PUT only. It has no direct submission, author-mine or content-edit action; users create and edit visits through `/api/v1/check-ins`.

---

## 2. Approve / reject: effect on photos

```http
PUT /api/v1/moderation/check-ins
Authorization: Bearer {moderatorJwt}
Content-Type: application/json

{ "submissionId": "11111111-1111-1111-1111-111111111111", "moderationStatus": "Approved", "comment": null, "rejectReason": null }
```

`moderationStatus` is `"Approved" | "Rejected" | "Pending"`. For rejections, `comment` (if non-blank) is used as the reason; otherwise `rejectReason` is used.

| Decision | Canonical public content and bytes | Snapshot / photo metadata | Media / MinIO |
|----------|------------------------------------|---------------------------|---------------|
| Current revision approved | Same check-in and its photos become publicly readable; no separate review copy is created | Unchanged | Unchanged |
| Current revision rejected / back to Pending | Public content, rating contribution and anonymous byte access are withdrawn | Photos retained | Unchanged |
| Stale, hidden or deleted revision decision | Cannot publish current content or photos | Immutable snapshot retained | Unchanged |

What this means in practice:

- Moderation decisions never delete or re-tag photos.
- Authors can read their retained photo bytes through `/api/v1/check-ins/{id}/photos/{photoId}` while their visit exists. Other readers use the same route only while the current revision is approved and public.
- Attached photos become permanent (`is_permanent=true`, Media status `Confirmed`) shortly after the check-in is saved (asynchronously via `CheckInPhotosAttachedEvent`), whatever the moderation outcome.

---

## 3. Shop gallery and menu photos (admin / owner)

Presign with the endpoint for the target surface, `PUT` each file to MinIO exactly as in [photos-client.md § 2](photos-client.md#2-upload-the-bytes-to-minio), then attach with the listed body.

| Surface | Presign | Attach | Remove | Reorder |
|---------|---------|--------|--------|---------|
| Admin shop gallery | `POST /api/photos/shop` | `POST /api/admin/shops/{id}/photos` | `DELETE /api/admin/shops/{id}/photos` | `PUT /api/admin/shops/{id}/photos/order` |
| Owner shop gallery | `POST /api/photos/shop` | `POST /api/owner/coffee-shops/{id}/photos` | `DELETE /api/owner/coffee-shops/{id}/photos` | `PUT /api/owner/coffee-shops/{id}/photos/order` |
| Admin shop menu | `POST /api/photos/menu` (keys `menus/…`) | `POST /api/admin/shops/{id}/menu/photos` | `DELETE /api/admin/shops/{id}/menu/photos` | — |
| Roaster photos | `POST /api/photos/roaster` (keys `roasters/…`) | via the roaster contribution / admin roaster flow | — | — |

Owner menu photo endpoints are not currently supported.

Bodies:

```ts
// POST .../photos
type AddCoffeeShopPhotosRequest = { photos: UploadedPhotoDto[] };      // at least one
// DELETE .../photos
type RemoveCoffeeShopPhotosRequest = { photoIds: string[] };           // ShopPhoto ids
// PUT .../photos/order
type ReorderCoffeeShopPhotosRequest = { photoIds: string[] };          // full new order
// POST .../menu/photos
type AttachMenuPhotosRequest = { photos: UploadedPhotoDto[] };
// DELETE .../menu/photos
type RemoveMenuPhotosRequest = { photoIds: string[] };
```

- Gallery add with an empty list returns **400** `At least one photo is required.`; an unknown shop returns **404** `Shop not found.`.
- Ordering semantics (cover = index 0, full-list reorder): see [frontend-shop-photo-order.md](../frontend-shop-photo-order.md).
- Gallery and menu attach endpoints use the `UploadedPhotoDto` values as sent. They are **not** checked against Media, and they do not mark the photos permanent. The ownership and single-use checks described above apply only to check-in photos.

---

## 4. `POST /api/photos/resolve` (service-to-service)

Read-only metadata lookup for the caller's pending uploads. The endpoint is reachable through the authenticated gateway (`/api/Photos/{**remainder}`, `media-upload` rate limit) and always scoped to the caller. Canonical check-in creation uses the trusted internal claim endpoint to bind uploaded photo IDs to the author/check-in; a resolve result alone does not authorize attachment.

```http
POST /api/photos/resolve
Content-Type: application/json

{ "storageKeys": ["check-ins/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg", "check-ins/unknown.jpg"] }
```

**200** — `ApiResponse<ResolvedUploadedPhotoDto[]>`

```ts
type ResolvedUploadedPhotoDto = {
  storageKey: string;
  fileName: string;     // from Media metadata (presign time)
  contentType: string;  // from Media metadata (presign time)
  sizeBytes: number;    // real MinIO object size
};
```

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": [
    { "storageKey": "check-ins/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg", "fileName": "latte.jpg", "contentType": "image/jpeg", "sizeBytes": 734003 }
  ]
}
```

Rules:

- The owner always comes from the authenticated principal; any owner in the body is ignored.
- A key is returned only if it belongs to the caller, is in the shop/check-in bucket, is not bound to a check-in, has status `Pending`, is no older than 24 h, and the object exists in MinIO. Other keys are **omitted**, not reported as errors.
- The endpoint is read-only and changes nothing.
- An empty list returns `isSuccess: false`, `message: "At least one storage key is required"`. More than 20 keys returns `isSuccess: false`, `message: "At most 20 storage keys can be resolved at once"`. In both cases the HTTP status is still 200 (the controller wraps the handler response in `Ok`).
