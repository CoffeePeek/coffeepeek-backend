# Check-ins API v1

Full current response files and slug examples are in [check-ins-examples.md](check-ins-examples.md).

The application-wide chronological publication feed is [GET /api/v1/feed](feed.md).
It reuses check-in cards and actions, with a separate publication-time cursor.

`/api/v1/check-ins` is the supported coffee-shop visit API. Private home and self-prepared brews use the separate [home check-in client contract](home-check-ins-client.md); [admin access and catalog operations](home-check-ins-admin.md) are documented separately. Shop visits require only the selected coffee shop (`coffeeShopSlug`) and visit date (`visitedAt`). Text, rating, drink and photos are optional for both Private and Public shop visits. Author identity comes from the authenticated account.

```json
{
  "coffeeShopSlug": "example-coffee",
  "visitedAt": "2026-10-01T09:00:00Z"
}
```

Visibility defaults to `0` (`Private`, author only); `1` (`Public`) requests moderation before publication. Required `visitedAt` is normalized to UTC; missing/null, invalid or future dates are rejected (the existing clock-skew tolerance applies). Optional text is trimmed and limited to 1,000 characters; missing/null or blank text is stored as an empty string. Omitted/null rating is returned as `null`; when supplied, it must contain all three integer scores (`coffee`, `service`, `place`) from 1 through 5. Optional `drinkSlug` selects an active catalog drink and freezes its Russian/English names. `drinkSlug: "other"` requires a trimmed `customDrinkName` of at most 100 characters; other selections must omit the custom name. Omitting both leaves the drink empty. Optional `photos` contains at most five distinct `UploadedPhotoDto` objects from the private `/api/Photos/check-in` upload flow. Media verifies ownership, actual uploaded bytes, supported raster MIME and unused metadata; client filename, MIME and size fields do not authorize attachment.

| Method and path | Behavior |
| --- | --- |
| `POST /api/v1/check-ins` | Create a visit with either visibility; only shop and date are required. |
| `GET /api/v1/check-ins/mine` | Author history; `pageNumber`, `pageSize` (max 100), optional half-open UTC visit range `from`/`to`; pagination headers included. |
| `GET /api/v1/check-ins/{id}` | Author reads their current visit; other readers see only current approved public content. Inaccessible and deleted IDs return 404. |
| `PUT /api/v1/check-ins/{id}` | Replace optional text, rating and drink; omitted/null values clear those fields. Preserve identity, visit time and ordered photos. Advance the revision and withdraw previous public approval. |
| `PUT /api/v1/check-ins/{id}/visibility` | Body `{"visibility":0}` or `{"visibility":1}`. Public visibility submits the current revision; repeating the current visibility is a no-op. |
| `DELETE /api/v1/check-ins/{id}` | Author soft deletion; remove public eligibility and rating contribution immediately. |
| `GET /api/v1/check-ins` | Public cursor feed; optional `citySlug`, `coffeeShopSlug`, `authorSlug`, `pageSize` (1–100) and opaque `cursor`. |
| `PUT` / `DELETE /api/v1/check-ins/{id}/helpful` | Idempotent vote on eligible public content; authors cannot vote on their own visit. |
| `POST /api/v1/check-ins/{id}/reports` | Report current approved public content; reporter identity comes from authentication. Historical report audit remains available to administrators; accepted reviews rebind to the transferred check-in, and unmapped reports keep their original target UUID even when the target is missing. |
| `GET /api/v1/check-ins/{id}/photos/{photoId}` | Protected photo bytes; current author/public visibility checked on every request. |
| `GET /api/v1/check-ins/{id}/moderation-photos/{photoId}?revision=N` | Moderator-only bytes for the current public submission revision. Private, deleted and stale revisions are inaccessible. |

Responses use the existing `Response<T>` envelope. Enums serialize by name (`Private`/`Public`, `NotSubmitted`/`Pending`/`Approved`/`Rejected`); numeric request values are also accepted. A visit exposes `id`, `contentRevision`, visibility, moderation state, text, scores, optional frozen drink names, shop/author public addresses, ordered protected photo URLs, helpful count and current reader vote. Rejection details are author-only. Mutable content and byte responses use no-store caching.

Every public check-in card also exposes nullable `authorAvatarUrl`, the author's current profile image URL from Account. It is returned in both feed APIs, detail, owner history, shop/user check-in lists and create/edit/visibility responses. It is `null` when the author has no avatar or their public profile is unavailable (including blocked or deleted accounts); the check-in itself is still returned according to its existing access rules. Avatar changes appear on subsequent reads without editing old check-ins.

Deploy Account before Shops for this field: Shops batches author profile reads through the trusted internal `GET /internal/public-addresses/users/profiles` endpoint. Account uses the same `MediaPublicUrlOptions` as public user profiles to construct avatar URLs. No database migration or avatar backfill is required.

Apply the `OptionalCheckInContent` migrations to both Shops and Moderation before deploying this contract. Existing text and scores are preserved. Clients must accept `rating: null` and include `visitedAt` on creation. Rolling back to mandatory content requires every stored visit and snapshot to have nonblank text and all scores; the rollback does not invent missing scores.

Creation enforces a three-hour cooldown per author and coffee shop: a recent check-in does not prevent visiting a different shop. The daily limit of six visits applies per author across all shops. Each successful canonical creation emits one persisted creation notification for Account `CheckInCount`; rejected creation does not emit it. There is no separate review counter. A photo set is single use: retrying exactly the same upload keys retains the canonical reservation identity after a lost Media reply. An already committed reservation returns 409. Ambiguous saves retain the reservation so compensation cannot release another concurrent attempt's bytes.

Only approval of the current public revision publishes a visit. Stale, duplicate or reordered decisions cannot publish hidden, deleted or edited content. The shop rating selects the latest eligible public score per author/shop by `VisitedAt`, then `CreatedAtUtc`, then canonical ID, descending. Visits without a rating do not affect averages or replace an earlier score. Public `CheckIns` and `CheckInCount` include every eligible visit, including visits without text or scores; hiding, deleting or clearing the latest score falls back to the previous eligible score.

## Moderation

The moderator policy protects every `/api/v1/moderation/check-ins` action. Snapshot records have a required canonical `checkInId`, `contentRevision` and visit time. Text can be empty and rating can be null. Snapshots are immutable; moderators change their status.

| Method and path | Behavior |
| --- | --- |
| `GET /api/v1/moderation/check-ins` | Queue with `page`, `pageSize` (1–100), optional `status`, `search` and `userId`; response data contains `items`, `totalItems`, `totalPages`, `currentPage`, `pageSize`. |
| `GET /api/v1/moderation/check-ins/{id}` | Read one canonical submission snapshot. |
| `PUT /api/v1/moderation/check-ins` | Change status using the submission ID; moderator identity comes from authentication. |

```json
{
  "submissionId": "22222222-2222-2222-2222-222222222222",
  "moderationStatus": "Approved",
  "comment": null,
  "rejectReason": null
}
```

Status values are `Pending`, `Approved` and `Rejected`. A rejection requires a reason. Snapshot photo URLs always point to the protected canonical moderation-photo route.

The former v2 check-in routes, unversioned check-in/review APIs, review upload action and review submission/edit adapters are removed. No compatibility aliases remain. EF migration history and the current deployment procedure are documented in [the migration guide](../migrations/unified-check-ins.md).
