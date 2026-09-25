---
phase: quick-260925-kjj
plan: 01
status: complete
commit: fb5da516
---

# Quick 260925-kjj Summary: check-in date range for calendar view

`GET /api/CheckIns` accepts optional `from` / `to` query params (ISO 8601 `DateTimeOffset`,
converted to UTC). Filter is on `VisitedAt`, half-open `[from, to)`. `from >= to` → 400.
Pagination headers unchanged; request `X-Page-Size: 100` for a month view.

## Tests
- `CheckInQueriesTests.GetByUserId_WithRange_ReturnsOnlyUsersCheckInsInHalfOpenRange` (InMemory, boundaries + other user excluded)
- `GetUserCheckInsHandlerTests.Handle_ForwardsDateRangeToQuery`

## Deferred
- No `(UserId, VisitedAt)` index — per-user check-in volume is small; add via migration if the query shows up slow.
