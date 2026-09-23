# Frontend: coffee shop gallery photo order

Admin (and owner) can reorder published shop gallery photos. Order is persisted as `sortIndex` (0 = cover / first in carousel).

Base URL: gateway host, e.g. `https://api.example.com` or Aspire `http://localhost:5000`.

---

## Auth

| Surface | Header | Policy |
|---------|--------|--------|
| Admin | `Authorization: Bearer {adminJwt}` | Admin |
| Owner | `Authorization: Bearer {ownerJwt}` | Owner (must own the shop) |

---

## Types

```ts
type AdminShopPhoto = {
  id: string;          // Guid — use this for reorder
  fileName: string;
  contentType: string;
  storageKey: string;
  fullUrl: string;
  sizeBytes: number;
  sortIndex: number;   // 0-based display order
};

type AdminPublishedShop = {
  id: string;
  name: string;
  cityId: string;
  status: number;
  creatorId: string;
  ownerUserId: string | null;
  moderationId: string | null;
  createdAtUtc: string;
  isHidden: boolean;
  photos: AdminShopPhoto[];  // already sorted by sortIndex ascending
};

type ApiResponse<T> = {
  isSuccess: boolean;
  message: string;
  data?: T;
};
```

Public shop DTOs (`ShortPhotoMetadataDto`) also expose additive `id` and `sortIndex` so public UIs can respect the same order.

---

## Load shop (with photos)

### Admin

```http
GET /api/admin/shops/{shopId}
Authorization: Bearer {adminJwt}
```

### Owner

```http
GET /api/owner/coffee-shops/{shopId}
Authorization: Bearer {ownerJwt}
```

**200** — `ApiResponse<AdminPublishedShop>`

Render `data.photos` in array order (backend already sorts by `sortIndex`). Treat `photos[0]` as the cover.

List endpoints (`GET /api/admin/shops`, `GET /api/owner/coffee-shops`) may return shops **without** photos loaded (`photos: []`). Always use get-by-id for the reorder UI.

---

## Save new order

### Admin

```http
PUT /api/admin/shops/{shopId}/photos/order
Authorization: Bearer {adminJwt}
Content-Type: application/json

{
  "photoIds": [
    "33333333-3333-3333-3333-333333333333",
    "11111111-1111-1111-1111-111111111111",
    "22222222-2222-2222-2222-222222222222"
  ]
}
```

### Owner

```http
PUT /api/owner/coffee-shops/{shopId}/photos/order
Authorization: Bearer {ownerJwt}
Content-Type: application/json

{
  "photoIds": [ /* same shape */ ]
}
```

### Rules

- `photoIds` must be a **full permutation** of that shop’s gallery photo IDs (same set, new order).
- No duplicates, no missing IDs, no foreign photo IDs.
- First ID becomes `sortIndex: 0` (cover).
- Empty gallery: send `[]` (no-op).

### Responses

| Status | When |
|--------|------|
| **200** | Success — body is `ApiResponse<AdminPublishedShop>` with updated `photos` / `sortIndex` |
| **400** | Incomplete / duplicate / unknown photo IDs |
| **401** / **403** | Missing or insufficient auth |
| **404** | Shop not found (or not owned, for owner route) |

---

## Recommended UI flow

1. `GET` shop by id → show photos in response order.
2. Drag-and-drop (or move up/down) locally; keep a dirty flag.
3. Enable **Save order** only when order ≠ loaded order.
4. `PUT .../photos/order` with ordered `id`s.
5. On success → replace local state from `data.photos` (or re-fetch).
6. On failure → revert UI order and show `message`.

Shared helper:

```ts
async function reorderShopPhotos(
  basePath: '/api/admin/shops' | '/api/owner/coffee-shops',
  shopId: string,
  photoIds: string[],
  token: string,
): Promise<AdminPublishedShop> {
  const res = await fetch(`${basePath}/${shopId}/photos/order`, {
    method: 'PUT',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ photoIds }),
  });
  const json = (await res.json()) as ApiResponse<AdminPublishedShop>;
  if (!res.ok || !json.isSuccess || !json.data) {
    throw new Error(json.message || `Reorder failed (${res.status})`);
  }
  return json.data;
}
```

---

## Do not

- Reorder by `fullUrl` / `storageKey` — only by `id`.
- Send a partial list of moved items.
- Rely on client-only order for public pages — persist via the API so search/details APIs return the new order.

---

## Admin / owner profile fields

Admin and the assigned owner can update location, contacts, weekly schedule, catalogs (equipment / beans / roasters / brew methods), and add or delete gallery photos.

`GET` by id returns the extra fields when collections are loaded (`location`, `contacts`, `schedules`, `equipmentIds`, `beanIds`, `roasterIds`, `brewMethodIds`, `description`, `priceRange`). List endpoints may omit catalogs.

### Update shop

Optional nested objects are applied only when present. Omit a field to leave it unchanged. Send `catalogs.equipmentIds: []` (or beans / roasters / brew methods) to clear that catalog. Send `schedules: []` to remove stored hours (active shops then count as always open).

#### Admin

```http
PUT /api/admin/shops/{shopId}
Authorization: Bearer {adminJwt}
Content-Type: application/json

{
  "name": "Coffee Joy",
  "description": "Specialty on Nemiga",
  "priceRange": 2,
  "status": 0,
  "location": {
    "cityId": "11111111-1111-1111-1111-111111111111",
    "address": "Nemiga 5, Minsk",
    "latitude": 53.9045,
    "longitude": 27.5615
  },
  "contacts": {
    "phoneNumber": "+375291112233",
    "email": "hello@shop.by",
    "siteLink": "https://shop.by",
    "instagramLink": "https://instagram.com/shop"
  },
  "schedules": [
    {
      "dayOfWeek": 1,
      "isClosed": false,
      "intervals": [{ "openTime": "08:00:00", "closeTime": "18:00:00" }]
    }
  ],
  "catalogs": {
    "equipmentIds": ["22222222-2222-2222-2222-222222222222"],
    "beanIds": [],
    "roasterIds": ["33333333-3333-3333-3333-333333333333"],
    "brewMethodIds": []
  }
}
```

Admin `contacts` is optional (owner already sends phone/email/site/instagram at the top level).

#### Owner

```http
PUT /api/owner/coffee-shops/{shopId}
Authorization: Bearer {ownerJwt}
Content-Type: application/json

{
  "name": "Coffee Joy",
  "description": "Specialty on Nemiga",
  "phoneNumber": "+375291112233",
  "email": "hello@shop.by",
  "siteLink": "https://shop.by",
  "instagramLink": "https://instagram.com/shop",
  "location": { "cityId": "...", "address": "Nemiga 5, Minsk", "latitude": 53.9045, "longitude": 27.5615 },
  "schedules": [ /* same ScheduleDto as admin */ ],
  "catalogs": { "equipmentIds": [], "beanIds": [], "roasterIds": [], "brewMethodIds": [] }
}
```

Catalog IDs must exist in `/api/Catalogs/*`. `cityId` must exist. Duplicate weekdays in `schedules` → **400**.

**200** — `ApiResponse<AdminPublishedShop>` with the updated shop. **400** validation. **404** shop missing (or not owned).

### Add gallery photos

Upload files through Media (`POST /api/Photos/shop`), then attach metadata:

```http
POST /api/admin/shops/{shopId}/photos
POST /api/owner/coffee-shops/{shopId}/photos
Content-Type: application/json

{
  "photos": [
    {
      "fileName": "front.jpg",
      "contentType": "image/jpeg",
      "storageKey": "shops/.../front.jpg",
      "size": 12345
    }
  ]
}
```

New photos are appended (`sortIndex` continues). Reorder afterwards if needed.

### Delete gallery photos

```http
DELETE /api/admin/shops/{shopId}/photos
DELETE /api/owner/coffee-shops/{shopId}/photos
Content-Type: application/json

{ "photoIds": ["11111111-1111-1111-1111-111111111111"] }
```

Unknown IDs → **400**. Remaining photos are reindexed from 0.
