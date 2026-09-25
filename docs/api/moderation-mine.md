# Client: my moderation submissions

How a signed-in user sees what happened to the things they sent to moderation: coffee shops, roasters, reviews and shop change requests.

Each `/mine` endpoint returns only the caller's own submissions, newest first. The user id comes from the access token. No query parameter can select another user's items.

Anonymous submissions (a shop or roaster sent without a token) have no owner, so they never show up in any `/mine` list.

Base URL: gateway host, e.g. `https://api.example.com` or Aspire `http://localhost:5000`.

---

## Auth

| Surface | Header | Policy |
|---------|--------|--------|
| `GET /api/ModerationShops/mine` | `Authorization: Bearer {userJwt}` | Any authenticated user |
| `GET /api/ModerationRoasters/mine` | `Authorization: Bearer {userJwt}` | Any authenticated user |
| `GET /api/ModerationReviews/mine` | `Authorization: Bearer {userJwt}` | Any authenticated user |
| `GET /api/ShopChangeRequests/mine` | `Authorization: Bearer {userJwt}` | Any authenticated user |

Calls without a valid token get `401`. No moderator role is needed.

Gateway paths are case-insensitive (`/api/moderationshops/mine` = `/api/ModerationShops/mine`).

---

## Types

```ts
type ApiResponse<T> = {
  isSuccess: boolean;
  message: string;
  data?: T;
};

// CoffeePeek.Contract.Enums.ModerationStatus, serialized as a number
enum ModerationStatus {
  Pending = 0,
  Approved = 1,
  Rejected = 2,
}
```

---

## Shared query parameters

| Param | Type | Default | Rules |
|-------|------|---------|-------|
| `page` | int | `1` | 1 or more |
| `pageSize` | int | `20` | 1 to 100 |
| `status` | `ModerationStatus` (number) | none (all statuses) | `0`, `1` or `2` |

On the shop, roaster and review `/mine` endpoints, a `page` or `pageSize` out of range returns `400` with a validation error. `ShopChangeRequests/mine` clamps them into range instead (see its section).

Example: `GET /api/ModerationShops/mine?status=2&page=1&pageSize=10` lists the caller's rejected shops.

## Paging headers

Every successful `/mine` response carries:

| Header | Meaning |
|--------|---------|
| `X-Total-Count` | Total matching items |
| `X-Total-Pages` | Total pages for the given `pageSize` |
| `X-Current-Page` | Echo of `page` |
| `X-Page-Size` | Echo of `pageSize` |

The same numbers are also in the response body.

---

## GET /api/ModerationShops/mine

The caller's coffee shop submissions.

Items are in `data.moderationShops[]` (`ModerationShopDto`). The fields that matter for status tracking:

| Field | Type | Meaning |
|-------|------|---------|
| `moderationStatus` | `ModerationStatus` | Current state |
| `rejectedReason` | string \| null | Moderator's reason; null unless rejected |
| `publishedShopId` | Guid \| null | Id of the live shop in the catalog (use it with the Shops endpoints). Null until the shop is published. It can stay null for a short time after `moderationStatus` turns `1`, while the Shops service creates the shop. |

The rest of the DTO (name, address, contacts, schedules, photos, menu, `dataCompletenessScore`, ...) is the same shape moderators see.

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "moderationShops": [
      {
        "id": "8b1f7a52-6a3e-4a4e-9a3f-0f0d7b6c1e21",
        "name": "Grain Coffee",
        "address": "Nezavisimosti Ave 1",
        "moderationStatus": 1,
        "rejectedReason": null,
        "publishedShopId": "c2d9e3f4-1111-4c5b-9e1a-7a6b5c4d3e2f",
        "userId": "5e0b2c3d-...",
        "shopPhotos": [],
        "dataCompletenessScore": 70
      },
      {
        "id": "0a9e7c11-2d3b-4f5a-8b6c-1d2e3f4a5b6c",
        "name": "Corner Cafe",
        "moderationStatus": 2,
        "rejectedReason": "Photos do not show the coffee bar",
        "publishedShopId": null
      }
    ],
    "totalItems": 2,
    "totalPages": 1,
    "currentPage": 1,
    "pageSize": 20
  }
}
```

---

## GET /api/ModerationRoasters/mine

The caller's roaster submissions.

Items are in `data.items[]` (`ModerationRoasterDto`):

| Field | Type | Meaning |
|-------|------|---------|
| `moderationStatus` | `ModerationStatus` | Current state |
| `rejectedReason` | string \| null | Moderator's reason; null unless rejected |

There is no published-roaster id. The moderation service does not store the id of the catalog roaster created on approval.

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "items": [
      {
        "id": "3f2e1d0c-...",
        "name": "North Roasters",
        "about": "Small-batch roastery",
        "moderationStatus": 2,
        "rejectedReason": "Please add a website or Instagram link",
        "cityId": null,
        "location": null,
        "contact": null,
        "photos": []
      }
    ],
    "totalItems": 1,
    "totalPages": 1,
    "currentPage": 1,
    "pageSize": 20
  }
}
```

---

## GET /api/ModerationReviews/mine

The caller's review submissions.

Items are in `data.reviewDtos[]` (`ModerationReviewDto`):

| Field | Type | Meaning |
|-------|------|---------|
| `moderationStatus` | `ModerationStatus` | Current state |
| `rejectedReason` | string \| null | Moderator's reason; null unless rejected |
| `moderatedAt` | ISO datetime \| null | When a moderator last decided |
| `shopId` | Guid | The shop the review is about |
| `createdAt` | ISO datetime | When it was submitted |

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "reviewDtos": [
      {
        "id": "a1b2c3d4-...",
        "header": "Great flat white",
        "comment": "Friendly barista, good beans",
        "userId": "5e0b2c3d-...",
        "userName": "alice",
        "shopId": "c2d9e3f4-1111-4c5b-9e1a-7a6b5c4d3e2f",
        "rating": { "place": 5, "service": 4, "coffee": 5 },
        "rejectedReason": null,
        "moderatedBy": "9f8e7d6c-...",
        "createdAt": "2026-09-20T10:15:00Z",
        "moderatedAt": "2026-09-21T08:00:00Z",
        "moderationStatus": 1,
        "photos": []
      }
    ],
    "totalItems": 1,
    "totalPages": 1,
    "currentPage": 1,
    "pageSize": 20
  }
}
```

---

## GET /api/ShopChangeRequests/mine

The caller's change requests for published shops. This endpoint already existed; it is listed here so all four lists are in one place.

Extra query parameters on top of `page`, `pageSize`, `status`:

| Param | Type | Meaning |
|-------|------|---------|
| `shopId` | Guid | Only requests for this published shop |
| `section` | `ShopChangeSection` (number) | Only requests for this section: `0` Photos, `1` Contacts, `2` Description, `3` Tags, `4` Roasters, `5` Equipment, `6` Menu, `7` BrewMethods |

Unlike the other three endpoints, out-of-range `page` / `pageSize` values are clamped (page to 1 or more, pageSize to 1..100) instead of returning `400`. When there are no items, `totalPages` is `0`.

Items are in `data.items[]` (`ShopChangeRequestDto`):

| Field | Type | Meaning |
|-------|------|---------|
| `id` | Guid | Request id |
| `shopId` | Guid | Published shop the change targets |
| `submittedByUserId` | Guid | Always the caller here |
| `section` | `ShopChangeSection` | Which part of the shop the change covers |
| `payload` | object | The proposed values for that section |
| `status` | `ModerationStatus` | Current state (note: `status`, not `moderationStatus`) |
| `reviewedByUserId` | Guid \| null | Moderator who decided |
| `reviewedAtUtc` | ISO datetime \| null | When it was decided |
| `rejectionReason` | string \| null | Moderator's reason (note: `rejectionReason`, not `rejectedReason`) |
| `createdAtUtc` | ISO datetime | When it was submitted |
| `updatedAtUtc` | ISO datetime \| null | Last edit |

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "items": [
      {
        "id": "d4c3b2a1-...",
        "shopId": "c2d9e3f4-1111-4c5b-9e1a-7a6b5c4d3e2f",
        "submittedByUserId": "5e0b2c3d-...",
        "section": 2,
        "payload": { "description": "Now open on Sundays" },
        "status": 0,
        "reviewedByUserId": null,
        "reviewedAtUtc": null,
        "rejectionReason": null,
        "createdAtUtc": "2026-09-24T12:00:00Z",
        "updatedAtUtc": null
      }
    ],
    "totalItems": 1,
    "totalPages": 1,
    "currentPage": 1,
    "pageSize": 20
  }
}
```

---

## Errors

| Status | When |
|--------|------|
| `400` | `page` / `pageSize` out of range (shop, roaster, review endpoints), or an unknown `status` / `section` value |
| `401` | Missing or invalid token |
