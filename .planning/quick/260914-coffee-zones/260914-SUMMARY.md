# Coffee zones and map clustering — summary

## Delivered

- Added the `CoffeeZone` aggregate with draft/published/archived lifecycle and soft circular geography.
- Added include, exclude, and primary shop membership overrides with a database-enforced single explicit primary per shop.
- Added moderator CRUD, status, membership preview/override, and non-persisting DBSCAN candidate endpoints.
- Extended `GET /api/Map` with optional zoom-aware clusters and published zones while preserving the legacy no-zoom JSON shape.
- Added deterministic grid clustering, Haversine membership, normalized-distance primary-zone selection, stable limits, and truncation metadata.
- Added EF migration `20260914133214_AddCoffeeZonesAndMembershipOverrides` with coordinate/radius/enum checks and required indexes.
- Regenerated Wolverine static handlers for production mode.

## Verification

- `dotnet ef migrations has-pending-model-changes ... --no-build`: no pending model changes.
- `dotnet test CoffeePeek.Backend.ci.slnf -c Debug --no-restore --no-build`: 905 passed.
- `dotnet test CoffeePeek.Shops.Infrastructure.Tests/...`: 4 passed.
- `dotnet test CoffeePeek.ShopsService.Tests/...`: 4 passed.
- `git diff --check`: passed.

Tests cover domain validation/lifecycle, override precedence and cross-city rejection, deterministic clusters, a 20,000-point viewport, bounded DBSCAN candidates, legacy response serialization, EF constraints/indexes, and controller authorization.

## Notes

- Existing analyzer/XML documentation warnings remain outside this change.
- Existing package audit warnings remain for `Microsoft.OpenApi` 2.0.0 and `OpenTelemetry.Api` 1.15.0.
