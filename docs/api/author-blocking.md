# Personal author blocking

Account owns a directed `UserBlocks(BlockerId, BlockedId, CreatedAtUtc)` relation. This is separate from the administrative `User.IsBlocked` ban. Public `authorId` is an opaque, stable UUID (`User.PublicAuthorId`), not the internal user UUID and not a slug. It survives display-name and slug changes.

## HTTP contract

All three endpoints require the current authenticated user and return the normal `Response<T>` envelope. Responses use `Cache-Control: no-store`; the client never supplies the blocker identity.

| Method | Route | Data |
| --- | --- | --- |
| GET | `/api/v1/me/blocks?pageSize=100&cursor=…` | `items`, `nextCursor`, `revision` |
| PUT | `/api/v1/me/blocks/{authorId}` | `authorId`, `isBlocked`, `revision` |
| DELETE | `/api/v1/me/blocks/{authorId}` | `authorId`, `isBlocked`, `revision` |

List items contain `authorId`, `userName`, `avatarUrl`, `address`, `createdAtUtc`. Page size is 1–100. A cursor is tied to the list revision; a changed list invalidates the cursor with validation error, so clients must restart at page one without publishing a partial list. Writes are idempotent. Self-block is rejected. Deleted authors remain in the block list with a tombstone and can be unblocked.

Check-ins and public profiles include the new `authorId`. A viewer opening a profile they blocked receives a minimal profile with `isBlockedByViewer=true`; the other direction returns the same generic 404 as an unavailable profile. Block status is private: the blocked author cannot enumerate who blocked them.

## Visibility and relationships

For authenticated viewers, a block in either direction removes the other user's posts from public feeds, shop check-ins, author feeds, direct check-in detail, protected check-in photo downloads, public profile favorites/activity, and connection lists. Filtering runs before paging. Self-only diary queries and moderation access keep their existing authorization. Aggregate shop ratings are not recalculated per viewer.

Both follow relations are deleted atomically with block creation. Block and follow writes take the same transaction-scoped advisory lock for the unordered user pair, preventing a racing follow from surviving a committed block. New follows and helpful votes are rejected while either direction is blocked. Unblock removes only the caller's relation and never restores follows. Existing helpful votes remain historical aggregates.

Anonymous public visibility remains available. Blocking is an authenticated product restriction, not a promise that public content becomes inaccessible anonymously or from another account. No notification is sent to the target. Reporting/moderation remains a separate operation.

Shops requests current exclusions from Account through `/internal/users/{viewerId}/excluded-authors`, protected by the existing signed gateway trust boundary and not routed by Gateway. This lookup has no cache and fails closed with 503 when unavailable or invalid. Shops never accesses the Account database directly.

## Release and verification

1. Apply Account migration `20261018074440_AddPersonalAuthorBlocks` (including unique public author IDs for existing users).
2. Deploy Account, Shops and Gateway together before enabling the client release. Production Wolverine handlers were regenerated for the visibility dependency.
3. Verify with accounts A/B/C: block A→B; check both authenticated directions and unaffected C; repeat PUT/DELETE; add B→A and confirm removing A→B leaves B→A effective.
4. Verify feed cursors, profile favorites/activity/connections, old detail/photo URLs, follow/vote rejection, logout/login, renamed and deleted targets.
5. Run `dotnet test CoffeePeek.Backend.ci.slnf`. PostgreSQL migration/concurrency tests require the repository's `COFFEEPEEK_LIVE_PG` gate. The new `AccountLiveAuthorBlockTests` exercises the migration and concurrent block/follow operations against a throwaway database.

Local validation on 2026-10-18: all 2,365 runnable .NET tests passed; 182 environment-gated integration tests skipped. Docker daemon/PostgreSQL were unavailable, so the live concurrency test is provided for CI but was not executed locally. No production database or service was changed.
