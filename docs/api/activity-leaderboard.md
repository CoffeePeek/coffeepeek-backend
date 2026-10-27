# Activity leaderboard API

## Read leaderboard

`GET /api/v1/leaderboard?period=week&date=2026-10-26&page=1&pageSize=20`

Available anonymously through Gateway. Optional authentication adds `me` and applies bilateral author blocks. Responses use the usual `Response<LeaderboardPageDto>` envelope and `Cache-Control: no-store`.

- `period`: `week` (default) or `month`.
- `date`: optional ISO calendar date within the desired period; defaults to today in Europe/Minsk. Supported years: 2000–2100.
- `page`: 1–10000, default 1; `pageSize`: 1–100, default 20. Invalid values return validation errors.
- `data`: `period`, `startsOn`, `endsOnExclusive`, `timeZone`, `items`, `totalCount`, `page`, `pageSize`, `me`.
- Each row: `rank`, public `authorId`, `username`, `avatarUrl`, optional public `address`, `points`, `additions`, `edits`, `checkIns`.
- `me` is the caller's row even when it is outside the selected page; null for anonymous, excluded, or inactive users. Empty periods return an empty list and zero count.

Weights are addition 3, approved edit 1, eligible check-in 2. Counts describe actions, not points. Weeks start on Monday; months are calendar months. Equal scores share a competition rank (1, 1, 3). Current Admin/Moderator roles, blocked and deleted accounts are excluded at read time, including earlier periods. No historical results are frozen.

## Check-in location

Existing `POST /api/v1/check-ins` accepts an optional nested `location` object:

```json
{
  "coffeeShopSlug": "example-coffee",
  "visitedAt": "2026-10-26T09:00:00Z",
  "visibility": "Public",
  "text": "Good coffee",
  "location": {
    "latitude": 53.9,
    "longitude": 27.5,
    "accuracyMeters": 12,
    "capturedAt": "2026-10-26T09:00:00Z"
  }
}
```

Obtain location at creation time. Do not use the cafe coordinates as the device location. A missing object remains compatible with old clients. Numeric coordinates outside valid ranges, poor accuracy, stale measurements, absent cafe coordinates, and distance above 50 m disable automatic credit without rejecting the check-in. Malformed JSON still follows normal API validation.

The server requires distance <= 50 m, accuracy > 0 and <= 50 m, and capture time between server time minus 5 minutes and plus 1 minute. Device and cafe coordinates are frozen on creation and cannot be replaced by check-in edits. Only a public, currently approved shop check-in counts. Home check-ins never count.

Public and own check-in responses add:

- `countsForLeaderboard`: current eligibility (the Account projection can lag behind it).
- `leaderboardStatus`: `Counted`, `AwaitingApproval`, `Private`, `Deleted`, `HomeCheckIn`, `ExcludedByAdministrator`, `MissingLocation`, `InvalidLocation`, `MissingShopLocation`, `InaccurateLocation`, `StaleLocation`, or `OutsideRadius`.

Raw coordinates are not included in public check-in or leaderboard responses. The moderation DTO includes `leaderboardGeoStatus`, `shopDistanceMeters`, `locationAccuracyMeters`, `locationCapturedAtUtc`, `leaderboardOverride` and `leaderboardReason`.

## Administrative eligibility decision

The existing `PUT /api/v1/moderation/check-ins` accepts optional fields:

```json
{
  "submissionId": "33333333-3333-4333-8333-333333333333",
  "moderationStatus": "Approved",
  "comment": null,
  "rejectReason": null,
  "countsForLeaderboard": true,
  "leaderboardReason": "Verified visit; device GPS was inaccurate"
}
```

`countsForLeaderboard: true` includes a location exception; `false` excludes the check-in. Both require an authenticated Admin and a trimmed reason of 1–1000 characters. A Moderator can still decide publication but cannot submit these fields. The server derives the actor and override permission from authenticated claims; JSON cannot supply them. Omit `countsForLeaderboard` to keep the current eligibility decision. There is no separate publication side effect when only eligibility changes.

A manual decision is audited with actor, timestamp and reason. A newer content revision resets the override. Old or repeated moderation messages cannot change the current revision or duplicate points. An included but rejected/pending/private/deleted check-in still receives no points.

## Counting and rollout

The source emits a grant only after an approved shop/roaster addition is actually published or a shop change is applied. Replays, rejected changes, failed application, and unchanged content do not add points. User roaster edits are deferred; administrative edits, imports, beans and home check-ins do not score.

Check-in edit/hide/delete/rejection emits a revocation. Restoring eligibility preserves the first credited timestamp and original week/month. Source timestamps are server-controlled; client `visitedAt` cannot select the score period. The journal uses a stable source key and increasing version and retains revocations to handle out-of-order delivery.

Apply the Account, Shops and Moderation EF migrations before starting updated services. Deploy all three consumers/producers together; mixed versions cannot reliably propagate eligibility decisions. The new fields are nullable or have safe defaults, and migrations retain existing content. No backfill job, contest, prize, or notification campaign is included.

Client work is separate: leaderboard screen, location collection and moderator controls. GPS values are supplied by the device; this check is not protection against deliberate GPS spoofing.
