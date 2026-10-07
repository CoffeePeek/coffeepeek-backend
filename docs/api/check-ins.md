# Check-ins API v1

Full current response files and slug examples are in [check-ins-examples.md](check-ins-examples.md).

`/api/v1/check-ins` is the sole supported visit API. Text is required (1–1,000 nonblank characters after trimming), together with three integer scores from 1 through 5: coffee, service and place. Author identity comes from the authenticated account. Drink and photos are optional.

```json
{
  "coffeeShopSlug": "example-coffee",
  "text": "A good visit",
  "rating": { "coffee": 4, "service": 5, "place": 3 },
  "visibility": 0
}
```

Visibility defaults to `0` (`Private`, author only); `1` (`Public`) requests moderation before publication. Optional `visitedAt` defaults to now and is normalized to UTC. Optional `drinkSlug` selects an active catalog drink and freezes its Russian/English names. `drinkSlug: "other"` requires a trimmed `customDrinkName` of at most 100 characters; other selections must omit the custom name. Omitting both leaves the drink empty. Optional `photos` contains at most five distinct `UploadedPhotoDto` objects from the private `/api/Photos/check-in` upload flow. Media verifies ownership, actual uploaded bytes, supported raster MIME and unused metadata; client filename, MIME and size fields do not authorize attachment.

| Method and path | Behavior |
| --- | --- |
| `POST /api/v1/check-ins` | Create a complete visit with either visibility. |
| `GET /api/v1/check-ins/mine` | Author history; `pageNumber`, `pageSize` (max 100), optional half-open UTC visit range `from`/`to`; pagination headers included. |
| `GET /api/v1/check-ins/{id}` | Author reads their current visit; other readers see only current approved public content. Inaccessible and deleted IDs return 404. |
| `PUT /api/v1/check-ins/{id}` | Replace text, all scores and optional drink; preserve identity, visit time and ordered photos. Advance the revision and withdraw previous public approval. |
| `PUT /api/v1/check-ins/{id}/visibility` | Body `{"visibility":0}` or `{"visibility":1}`. Public visibility submits the current revision; repeating the current visibility is a no-op. |
| `DELETE /api/v1/check-ins/{id}` | Author soft deletion; remove public eligibility and rating contribution immediately. |
| `GET /api/v1/check-ins` | Public cursor feed; optional `citySlug`, `coffeeShopSlug`, `authorSlug`, `pageSize` (1–100) and opaque `cursor`. |
| `PUT` / `DELETE /api/v1/check-ins/{id}/helpful` | Idempotent vote on eligible public content; authors cannot vote on their own visit. |
| `POST /api/v1/check-ins/{id}/reports` | Report current approved public content; reporter identity comes from authentication. Historical report audit remains available to administrators. |
| `GET /api/v1/check-ins/{id}/photos/{photoId}` | Protected photo bytes; current author/public visibility checked on every request. |
| `GET /api/v1/check-ins/{id}/moderation-photos/{photoId}?revision=N` | Moderator-only bytes for the current public submission revision. Private, deleted and stale revisions are inaccessible. |

Responses use the existing `Response<T>` envelope. Enums serialize by name (`Private`/`Public`, `NotSubmitted`/`Pending`/`Approved`/`Rejected`); numeric request values are also accepted. A visit exposes `id`, `contentRevision`, visibility, moderation state, text, scores, optional frozen drink names, shop/author public addresses, ordered protected photo URLs, helpful count and current reader vote. Rejection details are author-only. Mutable content and byte responses use no-store caching.

Creation retains the three-hour cooldown and three-visits-per-day limit. Each successful canonical creation emits one persisted creation notification for Account `CheckInCount`; rejected creation does not emit it. There is no separate review counter. A photo set is single use: retrying exactly the same upload keys retains the canonical reservation identity after a lost Media reply. An already committed reservation returns 409. Ambiguous saves retain the reservation so compensation cannot release another concurrent attempt's bytes.

Only approval of the current public revision publishes a visit. Stale, duplicate or reordered decisions cannot publish hidden, deleted or edited content. The shop rating selects the latest eligible public score per author/shop by `VisitedAt`, then `CreatedAtUtc`, then canonical ID, descending. Public `CheckIns` and `CheckInCount` include every eligible visit; hiding or deleting the latest score falls back to the previous eligible score.

## Moderation

The moderator policy protects every `/api/v1/moderation/check-ins` action. Snapshot records have a required canonical `checkInId`, `contentRevision`, text, three scores and visit time. Snapshots are immutable; moderators change their status.

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

The former v2 check-in routes, unversioned check-in/review APIs, review upload action and review submission/edit adapters are removed. No compatibility aliases remain. The prepared database cleanup and tester-record deletion scope are documented in [the migration guide](../migrations/unified-check-ins.md).
