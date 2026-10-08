# Public check-in feed MVP

`GET /api/v1/feed` returns current approved public check-ins from all users. It is
anonymous; an optional Bearer token personalizes `checkIn.isHelpfulByCurrentUser`
without changing feed membership. Responses use `Cache-Control: no-store`.

## Request

```http
GET /api/v1/feed?pageSize=20
Authorization: Bearer <optional-token>
```

| Query parameter | Contract |
| --- | --- |
| `pageSize` | Integer, default 20, range 1–100. Invalid values return 400. |
| `cursor` | Opaque URL-safe continuation token from the previous page; omit on first load or refresh. |
| `citySlug` | Optional city filter. |
| `coffeeShopSlug` | Optional coffee shop filter. |
| `authorSlug` | Optional author filter. |

Filters are combined with AND. Slugs contain 1–100 characters. Unknown filters
return 404 rather than silently broadening the feed. Aliases resolve to canonical
identities. A cursor is bound to those identities; changing filters requires a
fresh request without a cursor. Cursor version/format/scope errors return 400.
Page size can change between continuation requests.

## Response

HTTP 200, including an empty feed:

```ts
type FeedResponse = {
  data: {
    items: Array<{
      publishedAtUtc: string;
      checkIn: PublicCheckInDto;
    }>;
    nextCursor: string | null;
  };
  isSuccess: boolean;
  message: string;
  statusCode: number | null;
};
```

`PublicCheckInDto` is the same contract used by [check-ins v1](check-ins.md):
shop/author public addresses, username, shop name, text, three ratings, visit and
creation times, optional drink, ordered protected photo URLs and helpful votes.
The full synthetic response is [examples/feed.json](examples/feed.json).

Items are ordered by `publishedAtUtc DESC, checkIn.id DESC`.
`publishedAtUtc` is the first approval time, not visit or creation time. A delayed
first approval appears among recent publications. Editing, hiding and republishing
do not reset that time or bump the item to the top.

The feed includes only nondeleted Public check-ins approved at their current
content revision, belonging to a visible existing coffee shop. Even authors do
not see their own private/pending/rejected content here; use `/api/v1/check-ins/mine`.
Check-ins are not grouped by author or shop: repeated eligible visits remain
separate feed items.

## Pagination and client behavior

- Append `items` and use `nextCursor` for infinite scrolling. `null` means there
  are no further items at the time of that read; an empty feed has `items: []` and
  `nextCursor: null`.
- Refresh or change filters by requesting the first page without a cursor.
  Newly published items above the current position appear on refresh.
- Keyset pagination works even if the last item of a previous page was deleted.
  Concurrent inserts above that position do not duplicate the next page.
- Each page checks current visibility. This is a live feed, not a frozen snapshot:
  content may be edited, withdrawn, approved or deleted between requests.
- Use the existing `/api/v1/check-ins/{id}` APIs for detail, helpful votes, reports,
  author edits and deletion. Render `photos[].url`; byte access is rechecked by
  the existing protected photo endpoint.

400 covers invalid page size, malformed/stale/mismatched cursor or invalid slug;
404 covers unknown filters. Errors use the existing error response contract.
As with other public endpoints, authentication errors may have no JSON body.

## Architecture and rollout

The feed is a read model in the Shops bounded context, not a new write aggregate
or a duplicate check-in table. `FeedController` dispatches `GetCheckInFeedQuery`;
the Application handler validates inputs, resolves public filters, handles the
versioned cursor and composes public addresses. It depends on the narrow
`ICheckInFeedReader` and `IPublicCheckInProjector` ports.

Persistence implements chronological reading with a bounded, no-tracking keyset
query. Existing check-in reads and the feed share `CheckInEligibility`,
`CheckInProjection` and `PublicCheckInProjector`: moderation and byte access rules
remain with canonical check-ins. Photos, shops, votes and author addresses are
read in batches, without one query per item or a total-count query.

A partial descending index on `(FirstPublishedAtUtc, Id)` supports the public
timeline. Apply the Shops `PublishedCheckInFeedIndex` EF migration during normal
deployment. It adds an index and does not copy or delete check-in data.

Future ranking, following or a separate read store can extend/replace the reader
while retaining canonical moderation and mutation APIs. New ordering semantics
must use a new cursor version. The MVP includes chronological global discovery
and the three optional filters; recommendation ranking and real-time delivery
are future features.

The existing `GET /api/v1/check-ins` retains its `CreatedAtUtc` ordering and cursor
format. Feed cursors and check-in-list cursors are not interchangeable.
OpenAPI is published through the gateway at `/shops/openapi/v1.json`.
