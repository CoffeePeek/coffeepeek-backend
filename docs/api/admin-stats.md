# Admin: dashboard statistics

Read-only statistics for the admin dashboard: user engagement (DAU/WAU/MAU), per-day timeseries, rating and top-shop insights, app downloads, and moderation SLA / queues / workload.

Base URL: gateway host, e.g. `https://api.example.com` or Aspire `http://localhost:5000`.

---

## Auth

Every endpoint in this document requires a JWT with the **Moderator** or **Admin** role:

```
Authorization: Bearer {jwt}
```

| Situation | Result |
|-----------|--------|
| No / invalid / expired token | `401 Unauthorized` (gateway) |
| Valid token without either the `Moderator` or `Admin` role | `403 Forbidden` (gateway; the service re-checks the same policy) |

Gateway routing:

| Path prefix | Service |
|-------------|---------|
| `/api/admin/stats/overview`, `/api/admin/stats/users/*` | Account (via a dedicated GET-only `/api/admin/stats/{**}` route) |
| `/api/admin/stats/shops/*` | Shops |
| `/api/admin/stats/moderation/*` | Moderation |

Moderators and administrators can also read `GET /api/admin/users` (including search,
role filters and pagination) and `GET /api/admin/users/stats`. User mutations
(role changes, blocking and deletion), session reads and session revocation remain
restricted to **Admin**. Existing URLs and response contracts are unchanged.

## Web integration changes — 2026-10-24

This update expands authorization. HTTP methods, URLs, query parameters, JSON DTOs,
field meanings and pagination headers keep their existing contracts.

The following requests previously required `Admin` and now accept either `Moderator`
or `Admin` through the gateway and in the owning service:

| Method | URL | Data available to the web client |
|--------|-----|----------------------------------|
| GET | `/api/admin/stats/overview` | All overview cards: users, activity, shops, check-ins, moderation and imports |
| GET | `/api/admin/stats/users/timeseries?days=30` | User registration chart |
| GET | `/api/admin/stats/shops/timeseries?days=30` | Shop and check-in charts |
| GET | `/api/admin/stats/shops/insights` | Ratings, top shops and app downloads |
| GET | `/api/admin/stats/moderation/insights` | Moderation SLA, queues and moderator workload |
| GET | `/api/admin/users` | User list with the existing search, role filter and pagination |
| GET | `/api/admin/users/stats` | Aggregate user counts and distribution by role |

For timeseries, `days` still defaults to `30` and is clamped to `1..90`.
`GET /api/admin/users` still accepts `page` (default `1`, minimum `1`),
`pageSize` (default `20`, range `1..100`), optional `search` (up to 100 characters)
and optional `role` (up to 50 characters). The response remains
`ApiResponse<{ items, totalItems, totalPages, currentPage, pageSize }>` with
`X-Total-Count`, `X-Total-Pages`, `X-Current-Page` and `X-Page-Size` headers.
Each item retains `id`, `userName`, `email`, `createdAtUtc`, `about`, `avatarUrl`,
`checkInCount`, `addedShopsCount`, `roles` and `isBlocked`.
`GET /api/admin/users/stats` still returns
`ApiResponse<{ totalUsers, activeUsers, blockedUsers, registeredToday, usersByRole }>`.

The following requests continue to require `Admin`; a `Moderator` receives `403`:

| Method | URL | Action |
|--------|-----|--------|
| PATCH | `/api/admin/users/{id}/role` | Change a user's role |
| PATCH | `/api/admin/users/{id}/block` | Block or unblock a user |
| DELETE | `/api/admin/users/{id}` | Delete a user |
| GET | `/api/admin/users/{userId}/sessions` | Read a user's sessions |
| DELETE | `/api/admin/users/{userId}/sessions/{sessionId}` | Revoke a session |
| DELETE | `/api/admin/users/{userId}/sessions` | Revoke all sessions |

Web changes required:

- Allow both roles in navigation and route guards for the complete dashboard and
  user list, including every dashboard widget and chart.
- Use the existing API calls, DTOs and bearer token. A moderator receives the same
  response shape and available data as an administrator for the allowed reads.
- Show user management controls and session views only to `Admin`. For moderators,
  render the user list as read-only, including role and blocked-status fields.
- Keep the usual `401` handling for missing/invalid/expired tokens and `403` handling
  for insufficient roles. Other roles continue to receive `403` on these reads.

Service-to-service `GET /api/admin/stats/summary` endpoints in Shops and Moderation
also accept both roles so overview aggregation works for moderators. The web client
continues to use `/api/admin/stats/overview` for those totals.

---

## Response envelope

All endpoints return HTTP `200` with the standard envelope (camelCase JSON):

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": { },
  "statusCode": null
}
```

Conventions used below:

| Kind | JSON form |
|------|-----------|
| Date (`DateOnly`) | `"2026-09-25"` — a UTC calendar day |
| Guid | string, e.g. `"3f2b…"` |
| `decimal` / `double` | number |
| Nullable number | `number` or `null` |

Time windows:

- **UTC day** — `00:00:00`–`23:59:59` UTC. "Today" = the current UTC date.
- **Rolling N days** — `now - N days` up to now (not aligned to midnight).
- **Timeseries window** — `days` consecutive UTC days ending today (inclusive): `today - (days - 1)` … `today`.

Rounding: hours are rounded to **1 decimal**, rating averages to **2 decimals** (half away from zero).

---

## GET /api/admin/stats/overview

Platform-wide totals. Always `200`: if the Shops or Moderation service is unreachable, its numbers are `0` and `shopsAvailable` / `moderationAvailable` is `false`.

Query params: none.

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "totalUsers": 1240,
    "usersRegisteredToday": 7,
    "activeUsers": 1198,
    "blockedUsers": 12,
    "totalCoffeeShops": 312,
    "totalCheckIns": 2045,
    "pendingModerationShops": 4,
    "pendingModerationCheckIns": 18,
    "newCoffeeShopsToday": 1,
    "newCheckInsToday": 23,
    "import": { "pending": 120, "published": 80, "rejected": 40, "skipped": 6, "inFeed": 75 },
    "shopsAvailable": true,
    "moderationAvailable": true,
    "dailyActiveUsers": 143,
    "weeklyActiveUsers": 512,
    "monthlyActiveUsers": 901,
    "deletedUsers": 30,
    "emailConfirmedUsers": 1100,
    "googleUsers": 420
  },
  "statusCode": null
}
```

| Field | Meaning |
|-------|---------|
| `totalUsers` | All user rows, including blocked and soft-deleted |
| `usersRegisteredToday` | Users created since 00:00 UTC today |
| `activeUsers` | Users that are **not blocked and not soft-deleted** |
| `blockedUsers` | Blocked users that are **not** soft-deleted |
| `deletedUsers` | Soft-deleted users (blocked or not) |
| `dailyActiveUsers` | Distinct users that logged in or refreshed a session in the last **24 h** (rolling) |
| `weeklyActiveUsers` | Same, last **7 days** (rolling) |
| `monthlyActiveUsers` | Same, last **30 days** (rolling) |
| `emailConfirmedUsers` | Users with a confirmed email (Google sign-ups count as confirmed) |
| `googleUsers` | Users registered through Google (external OAuth provider) |
| `totalCoffeeShops` | Shop rows in the Shops service |
| `totalCheckIns` | All currently approved public visits in visible shops; repeated visits count individually |
| `newCoffeeShopsToday`, `newCheckInsToday` | Created since 00:00 UTC today; check-ins must also be currently approved/public in a visible shop |
| `pendingModerationShops`, `pendingModerationCheckIns` | Shop items / canonical check-in submission snapshots waiting in moderation |
| `import.*` | Shop import candidate counts by state |
| `shopsAvailable`, `moderationAvailable` | `false` when that service did not answer — its numbers are `0`, not real zeros |

Edge cases / changes:

- **Behavior change:** `blockedUsers` used to include soft-deleted users and `activeUsers` was `totalUsers - blockedUsers`. Now `blockedUsers` excludes deleted users, and `activeUsers + blockedUsers + deletedUsers = totalUsers`.
- Activity is derived from session (refresh token) creation, which happens on every login and every token refresh. A user with a long-lived access token and no refresh in the window is not counted.

---

## GET /api/admin/stats/users/timeseries

New user registrations per UTC day.

| Query param | Type | Default | Notes |
|-------------|------|---------|-------|
| `days` | int | `30` | Clamped to `1..90` (`0` → `1`, `500` → `90`) |

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "days": 3,
    "newUsers": [
      { "date": "2026-09-23", "count": 5 },
      { "date": "2026-09-24", "count": 0 },
      { "date": "2026-09-25", "count": 2 }
    ]
  },
  "statusCode": null
}
```

| Field | Meaning |
|-------|---------|
| `days` | Effective window after clamping |
| `newUsers` | Exactly `days` entries, ascending by date, last entry = today (UTC). Days without registrations are `0` (zero-filled) |

---

## GET /api/admin/stats/shops/timeseries

New shops and check-ins per UTC day.

| Query param | Type | Default | Notes |
|-------------|------|---------|-------|
| `days` | int | `30` | Clamped to `1..90` |

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "days": 3,
    "newShops": [
      { "date": "2026-09-23", "count": 0 },
      { "date": "2026-09-24", "count": 1 },
      { "date": "2026-09-25", "count": 0 }
    ],
    "newCheckIns": [
      { "date": "2026-09-23", "count": 30 },
      { "date": "2026-09-24", "count": 27 },
      { "date": "2026-09-25", "count": 11 }
    ]
  },
  "statusCode": null
}
```

| Field | Meaning |
|-------|---------|
| `newShops` | Published shops created per day |
| `newCheckIns` | All non-deleted check-ins created per day, including private/pending visits (by creation time, not the visit date) |

Both series have exactly `days` zero-filled entries with identical dates, ascending, ending today (UTC).

---

## GET /api/admin/stats/shops/insights

Ratings, top shops and app download statistics. Query params: none.

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "ratings": {
      "totalCheckIns": 3,
      "averageRating": 2.89,
      "averagePlace": 3.0,
      "averageService": 3.0,
      "averageCoffee": 2.67,
      "distribution": [
        { "stars": 1, "count": 1 },
        { "stars": 2, "count": 0 },
        { "stars": 3, "count": 1 },
        { "stars": 4, "count": 0 },
        { "stars": 5, "count": 1 }
      ]
    },
    "topShopsByCheckIns30Days": [
      { "shopId": "0b7f5d0e-7c56-4f5c-9a0e-6f3c1f0f7d11", "name": "Alpha Coffee", "count": 3 },
      { "shopId": "5d3c3a9e-1f5e-4e0e-8a53-1c1f2b0f9e22", "name": "Beta Roasters", "count": 1 }
    ],
    "downloads": {
      "total": 3,
      "last30Days": 2,
      "byChannel": [
        { "channel": "android", "total": 2, "last30Days": 2 },
        { "channel": "ios", "total": 1, "last30Days": 0 }
      ],
      "topCountries30Days": [
        { "country": "DE", "count": 1 },
        { "country": "unknown", "count": 1 }
      ]
    }
  },
  "statusCode": null
}
```

| Field | Meaning |
|-------|---------|
| `ratings.totalCheckIns` | Selected eligible scores: one latest approved public check-in per author/shop in a visible shop |
| `ratings.averageRating` | Mean of selected `(place + service + coffee) / 3` scores, 2 decimals; `0` when none are eligible |
| `ratings.averagePlace` / `averageService` / `averageCoffee` | Mean of each selected dimension (1..5), 2 decimals; `0` when none are eligible |
| `ratings.distribution` | **Always 5 buckets**, `stars` 1..5. Each selected score lands in the bucket of its average rounded to the nearest integer (4.67 → 5) |
| `topShopsByCheckIns30Days` | Up to 10 shops by all non-deleted check-ins created in the last **30 days (rolling)**, including private/pending visits, descending |
| `downloads.total` | All-time app download redirect events |
| `downloads.last30Days` | Redirect events in the last 30 days (rolling) |
| `downloads.byChannel` | Per channel (e.g. `android`, `ios`): all-time and last-30-days counts, ordered by `total` desc |
| `downloads.topCountries30Days` | Up to 10 countries in the last 30 days, descending |

Edge cases:

- Shops that no longer exist are dropped from the top lists (so a list may have fewer than 10 entries).
- Events without a country are grouped as `"unknown"`.
- Rating statistics use the latest current approved public visit per author/shop, ordered by visit time, creation time and ID descending. Hiding, deleting or withdrawing approval falls back to the previous eligible visit.
- Overview/public/feed counts include every eligible visit; `ratings.totalCheckIns` counts only the selected scores, so these totals can differ.

---

## GET /api/admin/stats/moderation/insights

Moderation SLA, pending queues and moderator workload. Query params: none.

```json
{
  "isSuccess": true,
  "message": "Operation successful",
  "data": {
    "sla": {
      "checkInsModerated30Days": 42,
      "avgCheckInModerationHours": 3.0
    },
    "queues": [
      { "queue": "shops", "pending": 1, "oldestPendingHours": 10.0 },
      { "queue": "checkIns", "pending": 1, "oldestPendingHours": 30.0 },
      { "queue": "roasters", "pending": 0, "oldestPendingHours": null },
      { "queue": "changeRequests", "pending": 1, "oldestPendingHours": 5.0 },
      { "queue": "issueReports", "pending": 1, "oldestPendingHours": 2.0 }
    ],
    "oldestPendingHours": 30.0,
    "topModerators30Days": [
      { "moderatorUserId": "8c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f", "total": 3, "approved": 2, "rejected": 1 },
      { "moderatorUserId": "1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d", "total": 1, "approved": 1, "rejected": 0 }
    ]
  },
  "statusCode": null
}
```

| Field | Meaning |
|-------|---------|
| `sla.checkInsModerated30Days` | Canonical submission snapshots currently approved/rejected with a decision in the last 30 days (rolling) |
| `sla.avgCheckInModerationHours` | Mean hours from snapshot creation to the moderation decision, 1 decimal; `null` when nothing was moderated in the window |
| `queues` | **Always 5 entries in this order:** `shops`, `checkIns`, `roasters`, `changeRequests`, `issueReports` |
| `queues[].pending` | Items currently waiting (issue reports: status `Submitted`) |
| `queues[].oldestPendingHours` | Age in hours of the oldest pending item, 1 decimal; `null` when the queue is empty |
| `oldestPendingHours` | Maximum `oldestPendingHours` across queues; `null` when all queues are empty |
| `topModerators30Days` | Up to 10 moderators by moderation audit-log actions in the last 30 days, ordered by `total` desc. `total` may exceed `approved + rejected` (e.g. "moved back to pending" actions) |

Notes:

- Only moderator user IDs are returned (no names/emails). Resolve display names via the admin users API if needed.
- `queues[checkIns].pending` uses the same definition as `pendingModerationCheckIns` in the overview.

---

## TypeScript

```ts
export interface ApiResponse<T> {
  isSuccess: boolean;
  message: string;
  data: T;
  statusCode: number | null;
}

// GET /api/admin/stats/overview
export interface AdminImportOverviewStatsDto {
  pending: number;
  published: number;
  rejected: number;
  skipped: number;
  inFeed: number;
}

export interface AdminOverviewStatsDto {
  totalUsers: number;
  usersRegisteredToday: number;
  activeUsers: number;
  blockedUsers: number;
  totalCoffeeShops: number;
  totalCheckIns: number;
  pendingModerationShops: number;
  pendingModerationCheckIns: number;
  newCoffeeShopsToday: number;
  newCheckInsToday: number;
  import: AdminImportOverviewStatsDto;
  shopsAvailable: boolean;
  moderationAvailable: boolean;
  dailyActiveUsers: number;
  weeklyActiveUsers: number;
  monthlyActiveUsers: number;
  deletedUsers: number;
  emailConfirmedUsers: number;
  googleUsers: number;
}

// Timeseries
export interface AdminDailyCountDto {
  date: string; // "YYYY-MM-DD", UTC day
  count: number;
}

export interface AdminUsersTimeseriesDto {
  days: number;
  newUsers: AdminDailyCountDto[];
}

export interface AdminShopsTimeseriesDto {
  days: number;
  newShops: AdminDailyCountDto[];
  newCheckIns: AdminDailyCountDto[];
}

// GET /api/admin/stats/shops/insights
export interface AdminRatingBucketDto {
  stars: number; // 1..5
  count: number;
}

export interface AdminRatingStatsDto {
  totalCheckIns: number;
  averageRating: number;
  averagePlace: number;
  averageService: number;
  averageCoffee: number;
  distribution: AdminRatingBucketDto[]; // always 5 entries
}

export interface AdminTopShopDto {
  shopId: string;
  name: string;
  count: number;
}

export interface AdminDownloadChannelDto {
  channel: string;
  total: number;
  last30Days: number;
}

export interface AdminDownloadCountryDto {
  country: string; // "unknown" when missing
  count: number;
}

export interface AdminAppDownloadStatsDto {
  total: number;
  last30Days: number;
  byChannel: AdminDownloadChannelDto[];
  topCountries30Days: AdminDownloadCountryDto[];
}

export interface AdminShopsInsightsDto {
  ratings: AdminRatingStatsDto;
  topShopsByCheckIns30Days: AdminTopShopDto[];
  downloads: AdminAppDownloadStatsDto;
}

// GET /api/admin/stats/moderation/insights
export type AdminModerationQueueName =
  | "shops"
  | "checkIns"
  | "roasters"
  | "changeRequests"
  | "issueReports";

export interface AdminModerationSlaDto {
  checkInsModerated30Days: number;
  avgCheckInModerationHours: number | null;
}

export interface AdminModerationQueueDto {
  queue: AdminModerationQueueName;
  pending: number;
  oldestPendingHours: number | null;
}

export interface AdminModeratorWorkloadDto {
  moderatorUserId: string;
  total: number;
  approved: number;
  rejected: number;
}

export interface AdminModerationInsightsDto {
  sla: AdminModerationSlaDto;
  queues: AdminModerationQueueDto[];
  oldestPendingHours: number | null;
  topModerators30Days: AdminModeratorWorkloadDto[];
}
```

### Merging timeseries

Users and shops timeseries come from different services. Request both with the same `days` and merge by `date` — both are zero-filled over the same UTC window, so the dates line up one-to-one:

```ts
const [users, shops] = await Promise.all([
  get<ApiResponse<AdminUsersTimeseriesDto>>(`/api/admin/stats/users/timeseries?days=${days}`),
  get<ApiResponse<AdminShopsTimeseriesDto>>(`/api/admin/stats/shops/timeseries?days=${days}`),
]);

const rows = users.data.newUsers.map((u, i) => ({
  date: u.date,
  newUsers: u.count,
  newShops: shops.data.newShops[i]?.count ?? 0,
  newCheckIns: shops.data.newCheckIns[i]?.count ?? 0,
}));
```

If one of the calls fails (service down), render the other series and show the missing one as unavailable rather than as zeros.
