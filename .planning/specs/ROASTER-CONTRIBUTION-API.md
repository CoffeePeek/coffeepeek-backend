# API Contract: Roaster Contribution

**Status:** implemented (backend)
**Audience:** client app team + admin panel team (separate clients — this repo is backend-only)
**Backend location:** `CoffeePeek.Shops.*` / `CoffeePeek.ShopsService` (public detail + admin CRUD), `CoffeePeek.Moderation.*` / `CoffeePeek.ModerationService` (submission + review), and `CoffeePeek.MediaService` (photo upload — section 2.1), routed through the Gateway
**Base URL:** `http://localhost:5000` (Aspire dev) / `https://api.coffeepeek.by` (production) — all paths below are relative to this
**Related:** `.planning/specs/ROASTER-CONTRIBUTION.md` (requirements), `.planning/quick/260909-roaster-contribution/` (implementation)

All JSON field names are **camelCase** (ASP.NET Core's default web JSON options). All enums serialize as **strings**, not integers.

---

## 1. Shared enums

### `ModerationStatus`

| Value | Meaning | Set by |
|---|---|---|
| `Pending` | Initial state on submission | system |
| `Approved` | Published into the live roaster catalog | admin |
| `Rejected` | Not accepted — see `rejectedReason` on the moderation record | admin |

Admins can only transition **to** `Approved` or `Rejected` on the status-change endpoint — `Pending` is not a valid target status (returns `400`).

---

## 2. Response envelope (all endpoints)

Every endpoint returns one of these two shapes.

**Success:**

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": { /* endpoint-specific, see below */ },
  "statusCode": null
}
```

**Error:**

```json
{
  "isSuccess": false,
  "message": "A roaster with this name already exists.",
  "data": null,
  "statusCode": 409,
  "errorCode": null,
  "errors": null
}
```

`errorCode`/`errors` are only populated for field-level validation failures raised by model binding (e.g. missing required `name`); most domain errors (duplicate name, not found, invalid status transition) just set `message` + `statusCode`.

HTTP status codes used: `200`/`201` (success), `400` (validation/domain error), `401` (not authenticated), `403` (wrong role), `404` (not found), `409` (duplicate roaster name).

---

## 2.1 Uploading photos (Media service — prerequisite for sections 3 and 6)

Every `photos` field in this API references files **already uploaded** to the Media service — you never send raw bytes to the roaster endpoints. Flow: request presigned upload URLs → `PUT` each file's bytes to its URL → pass the returned metadata into the roaster submit/create/update body.

```
POST /api/Photos/roaster
Authorization: Bearer <jwt>          (any authenticated user)
Content-Type: application/json
```

**Request body** — an array (upload several at once):

```json
[
  { "sizeBytes": 204800, "fileName": "roastery.jpg", "contentType": "image/jpeg" }
]
```

**Success response (`200`):**

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": [
    {
      "photoId": "c2d3e4f5-...",
      "uploadUrl": "https://media.coffeepeek.by/coffeepeek.shops/roasters/abc123.jpg?X-Amz-...",
      "storageKey": "roasters/abc123.jpg"
    }
  ],
  "statusCode": null
}
```

`PUT` each file's raw bytes to its `uploadUrl` (presigned, expires in ~10 minutes). Then build the roaster `photos` array from `fileName` + `contentType` + `storageKey` + the byte size — see sections 3 and 6. Roaster photos are stored in the **shop bucket** under a `roasters/` key prefix, so their public URLs resolve to `{mediaEndpoint}/coffeepeek.shops/roasters/…`.

---

## 3. Client API — submit a roaster

```
POST /api/ModerationRoasters
Authorization: Bearer <jwt>          (any authenticated user)
Content-Type: application/json
```

**Rate limit:** 15 requests/minute per IP (shared `moderation-submission` policy — same bucket as shop suggestions, reviews, and issue reports).

**Request body:**

```json
{
  "name": "Coffee Circus",
  "about": "Small-batch specialty roaster based in Minsk.",
  "cityId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "address": "1 Main St, Minsk",
  "instagramLink": "https://instagram.com/coffeecircus",
  "siteLink": "https://coffeecircus.by",
  "photos": [
    { "fileName": "roastery.jpg", "contentType": "image/jpeg", "storageKey": "moderation/roasters/abc123.jpg", "size": 204800 }
  ]
}
```

- `name` — required, max 100 characters.
- `about`, `instagramLink`, `siteLink` — all optional.
- `address` and `cityId` — optional, but **must be sent together**. If only one is present the roaster is created without a location (no address shown on the profile). If both are present, the backend geocodes the address server-side (Yandex) — see `isAddressValidated` in the response.
- `photos` — optional array. Each entry must reference a file **already uploaded** via the Media service (`POST /api/Photos/roaster`, see section 2.1) — pass back the `fileName`/`contentType`/`storageKey`/`size` that upload returned. This endpoint does not accept raw file bytes.
- Do **not** send a `userId` field — it's derived server-side from the auth token and ignored if sent.

**Success response (`201`):**

```json
{
  "isSuccess": true,
  "message": "The application has been accepted and will be reviewed by the moderator.",
  "data": {
    "roasterId": "b1f8c2b0-1234-4a4a-9abc-1234567890ab",
    "status": "Pending",
    "isAddressValidated": true
  },
  "statusCode": null
}
```

If an address was submitted but geocoding failed, `isAddressValidated` is `false` and the message changes to: *"The application has been accepted. Address coordinates could not be verified automatically and will be checked by a moderator."* This is not an error — the submission still succeeds with `201`.

**Error cases:**

| Status | When |
|---|---|
| `400` | `name` missing/blank or over 100 chars |
| `409` | `name` matches an existing **published** roaster (case-insensitive) |
| `401` | No/invalid bearer token |

---

## 4. Client API — roaster detail (public)

```
GET /api/roasters/{id}
```

No authentication required. Returns the full profile of a **published** roaster (i.e. one that has gone through moderation and been approved, or was created directly by an admin) — not visible for roasters still pending review.

**Success response (`200`):**

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "id": "b1f8c2b0-1234-4a4a-9abc-1234567890ab",
    "name": "Coffee Circus",
    "about": "Small-batch specialty roaster based in Minsk.",
    "location": {
      "address": "1 Main St, Minsk",
      "latitude": 53.9,
      "longitude": 27.5
    },
    "contact": {
      "instagramLink": "https://instagram.com/coffeecircus",
      "siteLink": "https://coffeecircus.by"
    },
    "photos": [
      {
        "id": "c2d3e4f5-...",
        "fileName": "roastery.jpg",
        "storageKey": "roasters/abc123.jpg",
        "fullUrl": "https://media.coffeepeek.by/coffeepeek.shops/roasters/abc123.jpg",
        "sortIndex": 0
      }
    ],
    "shops": [
      { "id": "d4e5f6a7-...", "name": "Grunwald Coffee" }
    ]
  },
  "statusCode": null
}
```

- `location`, `contact` are `null` if never set.
- `photos`, `shops` are `[]` if empty — never `null`.
- `shops` is the reverse lookup: coffee shops that list this roaster among the roasters they serve. This is informational only — it does not come from anything submitted on this roaster's own record.

**Error cases:**

| Status | When |
|---|---|
| `404` | No roaster with that id (including: it exists but is still `Pending` moderation) |

**Note:** there is also a lightweight catalog-picker endpoint, `GET /api/catalogs/roasters`, that already existed before this feature — it returns `{ id, name }[]` for every roaster and is used when a shop owner/admin picks which roasters a coffee shop carries. It is unchanged by this feature and does not include the new profile fields.

---

## 5. Admin API — review roaster submissions

All endpoints below require `Authorization: Bearer <jwt>` with role `Moderator` or `Admin`. No special rate limit beyond the global gateway policy.

### 5.1 List submissions

```
GET /api/ModerationRoasters?page=1&pageSize=20&status=Pending
```

Query params (all optional):

- `page` — default `1`
- `pageSize` — default `20`, max `100`
- `status` — filter by `ModerationStatus`

**Response (`200`):**

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "items": [
      {
        "id": "b1f8c2b0-...",
        "name": "Coffee Circus",
        "about": "Small-batch specialty roaster based in Minsk.",
        "cityId": "3fa85f64-...",
        "location": { "address": "1 Main St, Minsk", "latitude": 53.9, "longitude": 27.5 },
        "contact": { "instagramLink": "https://instagram.com/coffeecircus", "siteLink": "https://coffeecircus.by" },
        "photos": [
          { "id": "c2d3e4f5-...", "fileName": "roastery.jpg", "contentType": "image/jpeg", "storageKey": "moderation/roasters/abc123.jpg", "fullUrl": "https://media.coffeepeek.by/...", "sizeBytes": 204800, "ownerId": "9c1e...", "uploadedAt": "2026-09-09T07:12:00Z", "sortIndex": 0 }
        ]
      }
    ],
    "totalItems": 1,
    "totalPages": 1,
    "currentPage": 1,
    "pageSize": 20
  },
  "statusCode": null
}
```

Response also includes pagination in headers: `X-Total-Count`, `X-Total-Pages`, `X-Current-Page`, `X-Page-Size`.

Note this list does **not** include a `moderationStatus` or `rejectedReason` field per item today — the admin panel should track which filter (`status=`) it queried with to know what it's looking at, or call 5.2 for a single record's full state if needed. (Flag to backend if the panel needs `moderationStatus`/`rejectedReason` inlined on list items — small additive change.)

### 5.2 Get one submission

```
GET /api/ModerationRoasters/{id}
```

Same shape as one item in 5.1's `items` array, wrapped in `data` directly (not an array).

**Error:** `404` if the id doesn't exist.

### 5.3 Approve / reject a submission

```
PUT /api/ModerationRoasters/status?id={id}&status={status}&comment={comment}
```

Query params:

- `id` — required, the moderation roaster's id.
- `status` — required, `Approved` or `Rejected` (`Pending` is rejected with `400`).
- `comment` — optional. Used as the rejection reason when `status=Rejected`; ignored when approving. If omitted on reject, defaults to `"Rejected by moderator"`.

**Success response (`200`):**

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": null,
  "statusCode": null
}
```

This endpoint does **not** echo back the new/old status (unlike some other moderation endpoints in this API) — the panel already knows what it just requested.

On approve, the roaster is published into the live catalog asynchronously (via an internal event) — it may take a moment to appear at `GET /api/roasters/{id}` and in `GET /api/catalogs/roasters`. Re-approving an already-approved submission is a no-op (`200`, nothing re-published).

**Error cases:**

| Status | When |
|---|---|
| `404` | `id` doesn't exist |
| `400` | `status=Pending` or an unrecognized value; approving with an address that failed geocoding (`isAddressValidated: false`) — moderator must reject or the submitter must resubmit with a correct address |
| `400` | Rejecting and the effective reason is under 2 or over 1000 characters (only relevant if you pass a very short/long custom `comment`) |
| `403` | Caller isn't `Moderator`/`Admin` |

---

## 6. Admin API — direct roaster catalog management

Unlike section 5 (which handles *user-submitted* roasters awaiting review), this section lets an admin manage the published catalog directly — same endpoints that existed before this feature, now extended with the full profile fields.

All endpoints require `Authorization: Bearer <jwt>` with role `Moderator` or `Admin`.

### 6.1 Create

```
POST /api/admin/roasters
```

**Request body:**

```json
{
  "name": "Coffee Circus",
  "about": "Small-batch specialty roaster based in Minsk.",
  "cityId": "3fa85f64-...",
  "address": "1 Main St, Minsk",
  "latitude": 53.9,
  "longitude": 27.5,
  "instagramLink": "https://instagram.com/coffeecircus",
  "siteLink": "https://coffeecircus.by",
  "photos": [
    { "fileName": "roastery.jpg", "contentType": "image/jpeg", "storageKey": "roasters/abc123.jpg", "size": 204800 }
  ]
}
```

Every field except `name` is optional. **Note the difference from the client submit endpoint (section 3): here the admin passes `latitude`/`longitude` directly — there is no server-side geocoding on this endpoint.** If `address`/`cityId` are sent without coordinates, the roaster is saved with an unvalidated location (no lat/lng).

**Response (`200`):** `Response<RoasterDto>` — **note this returns only `{ id, name }`**, not the full profile. Follow up with `GET /api/roasters/{id}` (section 4) if the panel needs to display what was just saved.

**Error:** `409` if `name` already exists (case-insensitive).

### 6.2 Update

```
PATCH /api/admin/roasters/{id}
```

Same body shape as create, minus `name` being the only truly-required field (all others optional — see caveat below).

**Important — this is a full-replace PATCH, not a partial merge:** any field you omit is treated as `null`/cleared, not "leave unchanged." If you're editing an existing roaster in a form, populate the form from `GET /api/roasters/{id}` first and send back all fields (changed or not), including `photos` (omitting `photos` entirely leaves existing photos untouched — but sending `"photos": []` clears them).

**Response (`200`):** `Response<RoasterDto>` (`{ id, name }` only, same caveat as 6.1).

**Error:** `404` if `id` doesn't exist.

### 6.3 Delete

```
DELETE /api/admin/roasters/{id}
```

**Response (`200`):** `Response` (non-generic, `data: null`).

**Error:** `404` if `id` doesn't exist. Deleting does not currently check whether any coffee shops still reference this roaster — that link is simply left dangling for those shops until they're re-edited.

---

## 7. Notes for both teams

- **No resubmission flow.** If a roaster submission is rejected, the user must submit a brand-new one via section 3 — there's no "edit and resubmit the same record."
- **No roaster search/browse endpoint.** Only detail-by-id (section 4) and the existing `{id, name}` picker (`GET /api/catalogs/roasters`) exist. A browsable/filterable roaster directory is not built.
- **Photos always go through the Media service first.** Every `photos` field across sections 3 and 6 expects already-uploaded file metadata (`fileName`, `contentType`, `storageKey`, `size`), never raw bytes — upload via `POST /api/Photos/roaster` (section 2.1). Roaster photos are stored in the shop bucket under a `roasters/` prefix.
- **Coffee-shop detail responses surface carried roasters.** The coffee-shop detail endpoint returns each roaster a shop carries as `{ id, name, photoUrl }` — `photoUrl` is the roaster's cover photo (lowest `SortIndex`), or `null` if it has none. Use the `id` to deep-link to section 4 for the full profile. This is the forward direction; section 4's `shops` array is the reverse lookup.
- **Admin CRUD (section 6) response DTOs are intentionally thin (`{id, name}`).** If the admin panel needs the full saved profile back in the same round-trip (instead of a follow-up `GET`), ask backend — this was a deliberate scope cut, not an oversight, but it's a small additive change if needed.
