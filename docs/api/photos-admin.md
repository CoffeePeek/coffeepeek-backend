# Admin / moderator: photos

How photos show up for moderators and admins, what moderation decisions do to them, and which presign endpoint to use for each shop photo surface.

Base URL: gateway host, e.g. `https://api.example.com` or Aspire `http://localhost:5000`.

Client-side upload flow (presign → `PUT` to MinIO → attach): [photos-client.md](photos-client.md).

---

## Auth

| Surface | Header | Policy |
|---------|--------|--------|
| Moderation reviews (read, change status) | `Authorization: Bearer {moderatorJwt}` | Moderator |
| Admin shop gallery / menu | `Authorization: Bearer {adminJwt}` | Admin |
| Owner shop gallery | `Authorization: Bearer {ownerJwt}` | Owner (must own the shop) |
| Presign (`/api/photos/*`) | `Authorization: Bearer {jwt}` | Authenticated; rate limit `media-upload` = 10 / minute per client |

---

## Types

```ts
type ApiResponse<T> = { isSuccess: boolean; message: string; data?: T };

// Moderation review photo (PhotoMetadataDto)
type PhotoMetadataDto = {
  id: string;
  fileName: string;
  contentType: string;
  storageKey: string;
  fullUrl: string | null; // {MediaPublicUrlOptions.PublicEndpoint}/{ShopBucketName}/{storageKey}; null if not configured
  sizeBytes: number;
  ownerId: string;        // review author
  uploadedAt: string;
  sortIndex: number;
};

type ModerationReviewDto = {
  id: string;
  header: string | null;
  comment: string;
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

## 1. Review photos in moderation

Reviews created by **public check-ins** arrive through `CheckinCreatedEvent`. Their `photos` are the check-in photos after Shops has verified them against Media (see [photos-client.md](photos-client.md#3-create-the-check-in-with-the-photos)). As a result:

- every photo was presigned by the review author and actually uploaded;
- `fileName` / `contentType` are the presign-time values stored in Media, and `sizeBytes` is the real object size;
- no photo is shared with another check-in.

### List

```http
GET /api/ModerationReviews?page=1&pageSize=20&status=Pending
Authorization: Bearer {moderatorJwt}
```

**200** — `ApiResponse<GetAllModerationReviewsResponse>` with pagination headers `X-Total-Count`, `X-Total-Pages`, `X-Current-Page`, `X-Page-Size`.

### Detail

```http
GET /api/ModerationReviews/{id}
Authorization: Bearer {moderatorJwt}
```

**200** — `ApiResponse<ModerationReviewDto>`; **404** if not found.

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "id": "8d1c...",
    "comment": "Friendly baristas and a very balanced espresso.",
    "userName": "anna",
    "moderationStatus": "Pending",
    "photos": [
      {
        "id": "4f0e...",
        "fileName": "latte.jpg",
        "contentType": "image/jpeg",
        "storageKey": "reviews/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg",
        "fullUrl": "https://media.example.com/coffee.shops/reviews/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg",
        "sizeBytes": 734003,
        "ownerId": "…author id…",
        "uploadedAt": "2026-09-24T09:31:02Z",
        "sortIndex": 0
      }
    ]
  }
}
```

Render `fullUrl` directly; the object is in the public shop bucket.

> **Note:** `POST /api/ModerationReviews` (a user submitting a review directly, not through a check-in) still accepts a `photos: UploadedPhotoDto[]` body that is **not** verified against Media. Treat photos on reviews that did not come from a check-in with the pre-hardening level of trust.

---

## 2. Approve / reject: effect on photos

```http
PUT /api/ModerationReviews
Authorization: Bearer {moderatorJwt}
Content-Type: application/json

{ "moderationReviewId": "8d1c...", "moderationStatus": "Approved", "comment": null, "rejectReason": null }
```

`moderationStatus` is `"Approved" | "Rejected" | "Pending"`. For rejections, `comment` (if non-blank) is used as the reason; otherwise `rejectReason` is used.

| Decision | Moderation review photos | Published shop review | Check-in photos | Media / MinIO |
|----------|--------------------------|-----------------------|-----------------|---------------|
| Approved (first time) | Unchanged | Created in Shops **without photos**; photos are not copied to the published review | Unchanged | Unchanged |
| Approved again (after edit) | Unchanged | Text/rating updated and review restored; photos untouched | Unchanged | Unchanged |
| Rejected / back to Pending | Unchanged | If it was approved before, the published copy is hidden via `ModerationReviewEditedEvent` | Unchanged | Unchanged |

What this means in practice:

- Moderation decisions never delete or re-tag photos.
- Showing review photos on the published review is **not currently supported**. The photos stay visible on the author's check-in (`GET /api/checkins`) and in the moderation review.
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

Used by Shops during check-in creation. Shops calls Media directly over service discovery (`http://coffeepeek-mediaservice`) with `X-Gateway-Auth: {GatewayAuth:SecretKey}` and `X-User-Id: {check-in author}`. The endpoint is also reachable through the gateway (`/api/Photos/{**remainder}`, Authenticated, `media-upload` rate limit). It is safe for clients to call because it is always scoped to the caller.

```http
POST /api/photos/resolve
Content-Type: application/json

{ "storageKeys": ["reviews/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg", "reviews/unknown.jpg"] }
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
    { "storageKey": "reviews/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg", "fileName": "latte.jpg", "contentType": "image/jpeg", "sizeBytes": 734003 }
  ]
}
```

Rules:

- The owner always comes from the authenticated principal; any owner in the body is ignored.
- A key is returned only if it belongs to the caller, is in the shop bucket, has status `Pending`, is no older than 24 h, and the object exists in MinIO. Other keys are **omitted**, not reported as errors.
- The endpoint is read-only and changes nothing.
- An empty list returns `isSuccess: false`, `message: "At least one storage key is required"`. More than 20 keys returns `isSuccess: false`, `message: "At most 20 storage keys can be resolved at once"`. In both cases the HTTP status is still 200 (the controller wraps the handler response in `Ok`).
