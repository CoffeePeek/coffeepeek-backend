# City reference integrity (P09)

Deleting a city used by a shop, roaster, or coffee zone returns HTTP 409. A missing city returns 404. New or changed references to a deleted city are rejected by PostgreSQL foreign keys and mapped to HTTP 409. The three relationships use `ON DELETE RESTRICT`; none cascades into business data.

Before applying `20261008091000_ProtectCityReferences`, run `dev/check-city-references.sql` against the Shops database. It lists every orphaned `Shops.CityId`, `Roasters.CityId`, and `CoffeeZones.CityId` reference. The migration repeats the count check and fails before changing the schema if any orphan remains. Restore the verified missing `Cities` row or correct each affected reference explicitly; the migration never deletes or remaps data. `Roasters.CityId` can be null when a roaster has no location.

The repository checks references for a clear 409 response, and the database foreign keys settle concurrent delete/reference-create races. `CityRepository.DeleteIfUnusedAsync` currently calls `SaveChangesAsync` inside the repository so it can catch the specific FK violation and return 409. This is a temporary exception to the single commit boundary targeted by P06. When the write pipeline is unified, preserve its conflict translation and the active-transaction savepoint behavior verified by `CityReferenceLiveTests`. City cache invalidation still happens in the handler before Wolverine's outer commit; P08 must move it to durable post-commit invalidation.

The live PostgreSQL tests are gated by `COFFEEPEEK_LIVE_PG` and are part of the PostgreSQL CI gate. They cover restrictive deletion for all three references, rejected inserts after city deletion, and transaction recovery after an FK save failure.
