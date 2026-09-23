---
status: complete
---

# Coffee Shop Data Completeness — Summary

Implemented a persisted `DataCompletenessScore` (0-100) for coffee shops. Public search now ranks
active shops by score descending and uses name as a stable tie-breaker. The score is returned in
short, detailed, and legacy shop DTOs.

## Scoring

- Gallery photo: 15
- Menu with at least one present item or menu photo: 15
- At least one non-deleted review: 10
- Stored working schedule: 15
- Coffee details: 5 each for roaster, brew method, bean, and equipment (20 total)
- Contacts: 5 each for Instagram, phone, and website (15 total)
- At least one check-in: 10

## Persistence and freshness

- Migration `20260914064959_AddCoffeeShopDataCompleteness` adds the `smallint` column, 0-100 check
  constraint, and `(Status, DataCompletenessScore DESC, Name)` index.
- PostgreSQL functions calculate and refresh only the affected shop. Triggers cover shop contacts,
  gallery photos, menu rows/items/photos, reviews (including soft-delete), check-ins, schedules, and
  all four coffee-detail join tables.
- The migration backfills every existing shop once after trigger creation.
- Search cache invalidation was added for check-ins, review creation/deletion, and menu changes so
  ranking does not wait for the normal cache TTL.

## Verification

- `dotnet build CoffeePeek.Backend.ci.slnf -c Debug --no-restore`: passed, 0 errors.
- `dotnet test CoffeePeek.Backend.ci.slnf -c Debug --no-build --no-restore`: passed, 891 tests.
- `dotnet ef migrations has-pending-model-changes`: no pending model changes.
- `dotnet ef migrations script ...AddCoffeeShopDataCompleteness`: SQL generation passed.
- Migration was not applied to a live database because Docker/PostgreSQL was unavailable locally.

