# API Contract: Shop Data-Accuracy Reports

**Status:** implemented (backend)
**Audience:** client app team + admin panel team (separate clients — this repo is backend-only)
**Backend location:** `CoffeePeek.Moderation.*` / `CoffeePeek.ModerationService`, routed through the Gateway
**Base URL:** `http://localhost:5000` (Aspire dev) / `https://api.coffeepeek.by` (production) — all paths below are relative to this

All JSON field names are **camelCase** (ASP.NET Core's default web JSON options). All enums serialize as **strings**, not integers.

---

## 1. Shared enums

### `ShopIssueCategory`

| Value | Meaning |
|---|---|
| `OutdatedMenu` | Menu shown in-app no longer matches reality |
| `ShopClosed` | Shop is permanently closed |
| `IncorrectAddress` | Address/location is wrong |
| `WrongOpeningHours` | Opening hours are wrong |
| `IncorrectPhotos` | Photos don't match the shop |
| `Other` | Anything else — **requires `description`** |

### `ShopIssueReportStatus`

| Value | Meaning | Set by |
|---|---|---|
| `Submitted` | Initial state on creation | system |
| `Reviewed` | A moderator looked at it, no fix needed yet / acknowledged | admin |
| `Fixed` | The underlying data issue was corrected | admin |
| `Invalid` | Not a real issue / spam / duplicate | admin |

Admins can only transition **to** `Reviewed`, `Fixed`, or `Invalid` — `Submitted` is not a valid target status for the admin status-change endpoint (returns `400`).

---

## 2. Response envelope (all endpoints)

Every endpoint returns one of these two shapes.

**Success:**
```json
{
  "isSuccess": true,
  "message": "Entity created successfully",
  "data": { /* endpoint-specific, see below — omitted or null on plain CreateEntityResponse */ },
  "statusCode": null,
  "entityId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```
`entityId` only appears on the create-report response. List/status-change responses put their payload in `data` instead (see below).

**Error:**
```json
{
  "isSuccess": false,
  "message": "Description is required when category is Other.",
  "data": null,
  "statusCode": 400,
  "errorCode": "VALIDATION_FAILED",
  "errors": { "description": ["..."] }
}
```
`errors` is only populated for field-level validation errors (e.g. missing/invalid request body fields); most domain errors just set `message` + `errorCode`.

HTTP status codes used: `200` (success), `400` (validation/domain error), `401` (not authenticated), `403` (wrong role), `404` (report not found), `500` (unexpected).

---

## 3. Client API — submit a report

```
POST /api/ShopIssueReports
Authorization: Bearer <jwt>          (any authenticated user)
Content-Type: application/json
```

**Rate limit:** 15 requests/minute per IP (shared `moderation-submission` policy — same bucket as review/suggestion submissions).

**Request body:**
```json
{
  "shopId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "category": "IncorrectAddress",
  "description": null
}
```
- `shopId` — required, guid of the coffee shop.
- `category` — required, one of the `ShopIssueCategory` values above.
- `description` — optional string, 2–1000 chars if provided. **Required (non-blank) when `category` is `"Other"`** — omitting it returns `400` with message `"Description is required when category is Other."`.
- Do **not** send a `userId` field — it's derived server-side from the auth token and ignored/overwritten if sent.

**Success response (`200`):**
```json
{
  "isSuccess": true,
  "message": "Entity created successfully",
  "data": null,
  "statusCode": null,
  "entityId": "b1f8c2b0-...."
}
```
`entityId` is the new report's id; `data` is always `null` for this endpoint (the created report isn't echoed back).

**Error cases:**
| Status | When |
|---|---|
| `400` | `category=Other` with no/blank `description`; `description` over 1000 chars; `shopId` missing/empty |
| `401` | No/invalid bearer token |

---

## 4. Admin API — list & review reports

All admin endpoints require `Authorization: Bearer <jwt>` with role `Moderator` or `Admin`. No special rate limit beyond the global gateway policy.

### 4.1 List reports

```
GET /api/admin/shop-reports?page=1&pageSize=20&status=Submitted&shopId=<guid>
```
Query params (all optional, case-insensitive):
- `page` — default `1`
- `pageSize` — default `20`, max `100`
- `status` — filter by `ShopIssueReportStatus`
- `shopId` — filter to one shop's reports

**Response (`200`):**
```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "items": [
      {
        "id": "b1f8c2b0-....",
        "shopId": "3fa85f64-....",
        "reportedByUserId": "9c1e....",
        "category": "IncorrectAddress",
        "description": null,
        "status": "Submitted",
        "reviewedBy": null,
        "reviewedAt": null,
        "createdAtUtc": "2026-09-09T07:12:00Z"
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

### 4.2 Change a report's status

```
PUT /api/admin/shop-reports/{reportId}/status
Content-Type: application/json
```
**Request body:**
```json
{ "status": "Fixed" }
```
`status` must be `"Reviewed"`, `"Fixed"`, or `"Invalid"`. (`reportId` in the body, if sent, is ignored — the path segment is authoritative.)

**Success response (`200`):**
```json
{
  "isSuccess": true,
  "message": "Entity updated successfully",
  "data": "Fixed",
  "statusCode": null,
  "oldEntity": "Submitted"
}
```
`data` is the new status, `oldEntity` is the status before this call — useful for optimistic-UI diffing.

**Error cases:**
| Status | When |
|---|---|
| `404` | `reportId` doesn't exist |
| `400` | `status` is `"Submitted"` or not a recognized value |
| `403` | Caller isn't `Moderator`/`Admin` |

---

## 5. Notes for the admin panel

- There is currently **no dedicated "get one report" endpoint** — the list endpoint returns full report objects, so render a detail view from the list item you already have. Ask backend to add `GET /api/admin/shop-reports/{id}` if a deep-link/refresh-on-load flow turns out to need it.
- `reportedByUserId` is a raw user id, not a display name/email — resolving it to a human-readable identity (if the admin panel wants to show "reported by Jane Doe") requires a separate lookup against the Account service; that's not wired up yet.
