# GSD Debug Knowledge Base

Resolved debug sessions. Used by `gsd-debugger` to surface known-pattern hypotheses at the start of new investigations.

---

## moderation-roaster-status — Moderation roaster responses omitted status
- **Date:** 2026-09-21
- **Error patterns:** moderation roaster, response, missing status, ModerationStatus, DTO
- **Root cause:** `ModerationRoasterDto` omitted `ModerationStatus`, leaving convention mapping and JSON serialization with no destination member.
- **Fix:** Added the contract `ModerationStatus` property and focused mapping/serialization regression coverage.
- **Files changed:** CoffeePeek.Contract/Dtos/CoffeeShop/ModerationRoasterDto.cs, CoffeePeek.ModerationService.Tests/ModerationRoasterDtoContractTests.cs
---

## catalog-roaster-photos — Catalog roaster responses omitted photos
- **Date:** 2026-09-21
- **Error patterns:** catalog roasters, missing photo data, PhotoUrl, CoverPhoto, Photos
- **Root cause:** `QueryRoasterRepository.GetAll()` omitted `Include(r => r.Photos)`, so its no-tracking query materialized roasters with an empty photo navigation.
- **Fix:** Eager-loaded `Roaster.Photos` in the catalog repository path and added focused persistence regression coverage.
- **Files changed:** Directory.Packages.props, CoffeePeek.Shops.Infrastructure.Tests/CoffeePeek.Shops.Infrastructure.Tests.csproj, CoffeePeek.Shops.Infrastructure.Tests/Persistence/QueryRoasterRepositoryTests.cs, CoffeePeek.Shops.Persistance/Repositories/QueryRoasterRepository.cs
---
