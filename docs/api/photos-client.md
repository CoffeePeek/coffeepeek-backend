# Client: check-in / review photos

How a signed-in user attaches photos to a check-in (and, for public check-ins, to the review that goes to moderation).

Base URL: gateway host, e.g. `https://api.example.com` or Aspire `http://localhost:5000`.

---

## Auth

| Surface | Header | Policy |
|---------|--------|--------|
| Media presign (`/api/photos/*`) | `Authorization: Bearer {userJwt}` | Authenticated; rate limit `media-upload` = 10 requests / minute per client |
| Check-ins (`/api/checkins`) | `Authorization: Bearer {userJwt}` | Authenticated |
| MinIO upload (`PUT {uploadUrl}`) | none (the URL is presigned) | — |

Gateway paths are case-insensitive (`/api/photos/review` = `/api/Photos/review`).

---

## Types

```ts
type ApiResponse<T> = {
  isSuccess: boolean;
  message: string;
  data?: T;
};

type ErrorResponse = {
  isSuccess: false;
  message: string;       // exact strings listed in the error tables below
  errorCode?: string;
  errors?: Record<string, string[]>;
};

// POST /api/photos/review body item
type PhotoRequest = {
  sizeBytes: number;     // int
  fileName: string;      // extension is kept in the storage key
  contentType: string;   // e.g. "image/jpeg" — MUST be sent again on the PUT
};

type GenerateUploadUrlResponse = {
  photoId: string;       // Guid (Media metadata id)
  uploadUrl: string;     // presigned MinIO PUT url, valid 600 s
  storageKey: string;    // "reviews/{guid}{ext}" — the only value the check-in needs
};

// POST /api/checkins photo item (unchanged contract)
type UploadedPhotoDto = {
  fileName: string;      // ignored by the server
  contentType: string;   // ignored by the server
  storageKey: string;    // trusted: must come from /api/photos/review
  size: number;          // ignored by the server
};

type RatingDto = { place: number; service: number; coffee: number }; // 1..5 each

type CreateCheckInCommand = {
  coffeeShopId: string;
  isPublic: boolean;
  visitedAt: string;     // ISO-8601, not in the future (5 min clock skew allowed)
  note?: string | null;  // <= 500; required (10..500) when isPublic
  photos?: UploadedPhotoDto[] | null;
  rating: RatingDto;
  header?: string | null; // public only, 3..100 when present
};

type CreateCheckInResponse = { id: string };
```

---

## 1. Get presigned upload URLs

```http
POST /api/photos/review
Authorization: Bearer {userJwt}
Content-Type: application/json

[
  { "sizeBytes": 734003, "fileName": "latte.jpg", "contentType": "image/jpeg" },
  { "sizeBytes": 512000, "fileName": "bar.png",   "contentType": "image/png" }
]
```

**200** — `ApiResponse<GenerateUploadUrlResponse[]>`, one item per request item, in the same order:

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": [
    {
      "photoId": "0f5c3c0e-6a0d-4a57-9d1a-3f3a5c7a2f10",
      "uploadUrl": "https://media.example.com/coffee.shops/reviews/7b2d...e1.jpg?X-Amz-Algorithm=...",
      "storageKey": "reviews/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg"
    },
    {
      "photoId": "c1a4...",
      "uploadUrl": "https://media.example.com/coffee.shops/reviews/9e...png?X-Amz-Algorithm=...",
      "storageKey": "reviews/9e3f0a52-5a3b-4d8f-8e0c-2b1a6f4d9c77.png"
    }
  ]
}
```

- Each call creates a Media record owned by the caller with status `Pending`.
- `POST /api/photos/shop` is the legacy alias. It has the same body and response, but its keys have no prefix (`{guid}{ext}`). Keys from `/shop` are still accepted by check-ins. New clients should use `/review`.
- `sizeBytes` and `contentType` in this request are not enforced. The server stores the `contentType` you send here and uses it later as the trusted value.

---

## 2. Upload the bytes to MinIO

```http
PUT {uploadUrl}
Content-Type: image/jpeg
x-amz-tagging: is_permanent=False

<binary file body>
```

Both headers are part of the signature and must be sent exactly as shown:

| Header | Value |
|--------|-------|
| `Content-Type` | Exactly the `contentType` sent in step 1 for this item |
| `x-amz-tagging` | `is_permanent=False` (literal, capital `F`) |

- If a header is missing or different, MinIO rejects the upload with **403 `SignatureDoesNotMatch`**.
- The URL expires **600 s** after it was issued. After that, request a new one (step 1).
- Success is **200** from MinIO with an empty body. No call back to the API is needed.

---

## 3. Create the check-in with the photos

```http
POST /api/checkins
Authorization: Bearer {userJwt}
Content-Type: application/json

{
  "coffeeShopId": "5a0f2b7e-2c1d-4a6e-9b0f-1d2e3f4a5b6c",
  "isPublic": true,
  "visitedAt": "2026-09-24T09:30:00Z",
  "header": "Great flat white",
  "note": "Friendly baristas and a very balanced espresso.",
  "rating": { "place": 5, "service": 4, "coffee": 5 },
  "photos": [
    { "fileName": "latte.jpg", "contentType": "image/jpeg", "storageKey": "reviews/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg", "size": 734003 },
    { "fileName": "bar.png",   "contentType": "image/png",  "storageKey": "reviews/9e3f0a52-5a3b-4d8f-8e0c-2b1a6f4d9c77.png", "size": 512000 }
  ]
}
```

**200** — `ApiResponse<CreateCheckInResponse>`

```json
{ "isSuccess": true, "message": "Operation successful", "data": { "id": "2b9c..." } }
```

### What the server trusts

Only `storageKey` is used from each photo. `fileName` and `contentType` are replaced with the values Media stored at presign time. `size` is replaced with the real size of the uploaded object. You can keep sending the fields (the contract is unchanged), but their values have no effect.

The photo order in `photos` is kept.

### Photo rules

| Rule | Limit |
|------|-------|
| Max photos per check-in | **5** |
| Duplicates | Not allowed. Keys are compared ordinally (case-sensitive). |
| Storage key | Required, non-blank, at most 255 characters |
| Owner | The photo must have been presigned by the **same user** |
| Uploaded | The object must actually exist in MinIO (step 2 succeeded) |
| Age | Must be attached within **24 h** of presign; after that the photo counts as expired |
| Single use | A photo can be attached to one check-in only; after a successful check-in it is no longer `Pending` |

These rules apply to public and private check-ins alike. Check-ins **without** photos are unaffected and never call Media.

### Errors (photo-related)

All of these are **400** `ErrorResponse`, with `message` exactly as shown:

| `message` | Cause |
|-----------|-------|
| `A check-in can have at most 5 photos` | More than 5 items in `photos` |
| `Photo storage key is required` | A photo item is `null`, or its `storageKey` is empty, whitespace, or longer than 255 |
| `Duplicate photos are not allowed` | The same `storageKey` appears twice |
| `One or more photos were not found, do not belong to you, or were already used` | At least one key is unknown, belongs to another user, was already attached, is older than 24 h, was never uploaded to MinIO, or is not in the shop bucket |
| `Photos could not be verified, please try again` | Media could not be reached or rejected the lookup. This fails closed; retry later. |

When any of these occur, nothing is saved and nothing is sent to moderation.

Other check-in validation errors are also 400. The exact strings are: `Coffee shop not found`, `Check-ins at the same coffee shop can be created once every 3 hours`, `Check-ins are limited to 6 per day`, `VisitedAt cannot be in the future`, `Rating must be between 1 and 5`, `Note is required when checking in publicly`, `Note must be between 10 and 500 characters` (public), `Note must not exceed 500 characters` (private), `Header must be between 3 and 100 characters`.

---

## 4. After the check-in is saved

- Shops publishes `CheckInPhotosAttachedEvent` through its durable outbox. Media then:
  - re-tags each object `is_permanent=true` (replacing `is_permanent=False`), so any bucket lifecycle rule that targets temporary uploads no longer matches it;
  - sets each photo's status to `Confirmed`, so the key can never be attached again.

  This happens asynchronously, usually within seconds.
- The photos appear on the check-in, e.g. `GET /api/checkins` → `data.checkIns[].photos[]` (`ShortPhotoMetadataDto` with `fullUrl`).
- For **public** check-ins, a review is sent to moderation with the same photos, using the trusted metadata. See [photos-admin.md](photos-admin.md). When the review is approved, the published shop review gets the same photos, in upload order. They appear in shop details as `data.shopDto.reviews[].photos[]`: `{ fileName, contentType, storageKey, size, fullUrl }`. Render `fullUrl`; don't build URLs from `storageKey`. All review photos live in the shops bucket (`coffeepeek.shops`). There is no reviews bucket.

---

## 5. Full example

```bash
TOKEN="eyJ..."
API="https://api.example.com"

# 1) presign
curl -s -X POST "$API/api/photos/review" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '[{"sizeBytes":734003,"fileName":"latte.jpg","contentType":"image/jpeg"}]'
# -> data[0].uploadUrl, data[0].storageKey = "reviews/7b2d...e1.jpg"

# 2) upload (same Content-Type + tagging header, within 600 s)
curl -s -X PUT "$UPLOAD_URL" \
  -H "Content-Type: image/jpeg" \
  -H "x-amz-tagging: is_permanent=False" \
  --data-binary @latte.jpg

# 3) check in (within 24 h)
curl -s -X POST "$API/api/checkins" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{
        "coffeeShopId":"5a0f2b7e-2c1d-4a6e-9b0f-1d2e3f4a5b6c",
        "isPublic":false,
        "visitedAt":"2026-09-24T09:30:00Z",
        "rating":{"place":5,"service":4,"coffee":5},
        "photos":[{"fileName":"latte.jpg","contentType":"image/jpeg","storageKey":"reviews/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg","size":0}]
      }'

# Re-sending the same storageKey in another check-in now returns 400
# "One or more photos were not found, do not belong to you, or were already used".
```
