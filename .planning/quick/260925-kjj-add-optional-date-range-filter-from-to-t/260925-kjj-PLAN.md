---
phase: quick-260925-kjj
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CoffeePeek.ShopsService/Controllers/CheckInsController.cs
  - CoffeePeek.Shops.Application/Features/CheckIn/GetUserCheckIns/GetUserCheckInsCommand.cs
  - CoffeePeek.Shops.Application/Features/CheckIn/GetUserCheckIns/GetUserCheckInsHandler.cs
  - CoffeePeek.Shops.Application/Features/CheckIn/ICheckInQueries.cs
  - CoffeePeek.Shops.Persistance/Queries/CheckInQueries.cs
  - CoffeePeek.Shops.Application.Tests/Features/CheckIn/GetUserCheckIns/GetUserCheckInsHandlerTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Persistence/CheckInQueriesTests.cs
---

# Quick 260925-kjj: date-range filter for user check-ins (calendar view)

Extend the existing `GET /api/CheckIns` (current user's check-ins) with optional `from` / `to`
query parameters instead of adding a new endpoint.

## Task 1: Range filter end-to-end
- Controller: `[FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to`; 400 when `from >= to`;
  convert to UTC `DateTime` (Npgsql timestamptz requires Kind=Utc).
- Command/handler/query: pass `From`/`To`; filter on `VisitedAt` with half-open `[from, to)`.
- Pagination unchanged (client can request pageSize=100 for a month).

## Task 2: Tests
- InMemory `CheckInQueriesTests`: only check-ins of the user inside `[from, to)` are returned; boundaries respected.
- Update handler test mocks for new signature and assert range is forwarded.

## Verify
`dotnet test CoffeePeek.Shops.Application.Tests` and `dotnet test CoffeePeek.Shops.Infrastructure.Tests` green.
