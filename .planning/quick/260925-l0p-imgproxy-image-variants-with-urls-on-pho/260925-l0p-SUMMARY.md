---
phase: quick-260925-l0p
plan: 01
subsystem: media / shops / moderation / deploy
tags: [imgproxy, media, photos, contract, caddy, docker-compose]
requirements: [IMG-VARIANTS]
dependency_graph:
  requires: []
  provides:
    - "MediaStorageUrlBuilder.BuildVariantUrl"
    - "ImageVariantPresets (thumb/card/detail/full)"
    - "MediaPublicUrlOptions.ImageProxyEndpoint + OptionalHttpUrlAttribute"
    - "PhotoUrlsDto (Contract) + Urls on PhotoMetadataDto / ShortPhotoMetadataDto"
    - "imgproxy compose service + Caddy {MEDIA_DOMAIN}/img/* route"
  affects:
    - "Shops responses: shop list/details, check-ins, roaster detail/cover, menus"
    - "Moderation responses: shops, ModerationShopApprovedEvent ShopDto, reviews, roasters, menu drafts"
tech-stack:
  added:
    - "ghcr.io/imgproxy/imgproxy:latest (container, deploy only)"
  patterns:
    - "Mapster identity MapWith for factory-built nested DTOs"
    - "String-only static factory calls inside ProjectToType-translated maps"
key-files:
  created:
    - CoffeePeek.Shared.Kernel/ImageVariantPresets.cs
    - CoffeePeek.Shared.Kernel/Options/OptionalHttpUrlAttribute.cs
    - CoffeePeek.Contract/Dtos/PhotoUrlsDto.cs
    - CoffeePeek.Shops.Application.Tests/Media/MediaStorageUrlBuilderTests.cs
    - CoffeePeek.Shops.Application.Tests/Media/PhotoUrlsDtoTests.cs
    - CoffeePeek.Shops.Application.Tests/Media/MediaPublicUrlOptionsTests.cs
  modified:
    - CoffeePeek.Shared.Kernel/MediaStorageUrlBuilder.cs
    - CoffeePeek.Shared.Kernel/Options/MediaPublicUrlOptions.cs
    - CoffeePeek.Contract/Dtos/PhotoMetadataDto.cs
    - CoffeePeek.Contract/Dtos/ShortPhotoMetadataDto.cs
    - CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs
    - CoffeePeek.Shops.Application/Features/Catalogs/GetRoasterById/GetRoasterByIdHandler.cs
    - CoffeePeek.Shops.Application/Features/Menu/ShopMenuDtoFactory.cs
    - CoffeePeek.Moderation.Application/Mapper/MapsterConfiguration.cs
    - CoffeePeek.Moderation.Application/Mapper/MapsterConfiguration.ModerationRoaster.cs
    - CoffeePeek.Moderation.Application/Features/Menu/MenuDraftMapper.cs
    - CoffeePeek.Shops.Application.Tests/Mapper/MapsterConfigurationCoffeeShopTests.cs
    - CoffeePeek.Shops.Application.Tests/Features/Catalogs/GetRoasterById/GetRoasterByIdHandlerTests.cs
    - CoffeePeek.Shops.Infrastructure.Tests/Persistence/CheckInQueriesTests.cs
    - CoffeePeek.ModerationService.Tests/ModerationReviewDtoContractTests.cs
    - deploy/docker-compose.yml
    - deploy/Caddyfile
    - deploy/.env.example
    - dev/dev.env
decisions:
  - "Variant URL format is {ImageProxyEndpoint}/_/{preset}/plain/{bucket}/{key}; each bucket/key path segment is Uri.EscapeDataString-escaped, and slashes are kept."
  - "ImageProxyEndpoint is optional and validated by a new OptionalHttpUrlAttribute, because [Url] rejects an empty string. An empty value makes all four variants equal the plain public URL."
  - "PhotoUrlsDto has no required members, so ModerationShopApprovedEvent JSON deserialization stays lenient."
  - "Mapster maps call the string overload of PhotoUrlsDto.Create, which is safe inside ProjectToType. Direct initializers call the options overload."
metrics:
  duration: "20m"
  completed: "2026-09-25"
  tasks: 3
  commits: 6
---

# Phase quick-260925-l0p Plan 01: imgproxy Image Variants Summary

Photo DTOs now carry an additive `urls` object with thumbnail, card, detail and fullscreen links. Each link is an imgproxy presets-only URL like `https://media.coffeepeek.by/img/_/{thumb|card|detail|full}/plain/{bucket}/{key}`. If `ImageProxyEndpoint` is empty, all four links equal the plain MinIO URL. In production an unsigned, presets-only imgproxy container sits behind Caddy at `{MEDIA_DOMAIN}/img/*` and reads from MinIO.

## What was built

### Task 1: Kernel builder, presets, option, Contract DTO
Commits `ded42d59` (RED) and `ba4b3603` (GREEN).
- `MediaStorageUrlBuilder.BuildVariantUrl(imageProxyEndpoint, bucketName, storageKey, preset)` guards null/whitespace inputs and trims slashes, with no double slashes. It escapes path segments, e.g. `a@b c?.jpg` becomes `a%40b%20c%3F.jpg`. `BuildPublicUrl` behaves as before; it only gained XML docs.
- `ImageVariantPresets` holds the four preset names (`thumb`, `card`, `detail`, `full`) and is the only place they are defined. Its XML doc ties them to `IMGPROXY_PRESETS` in the compose file.
- `OptionalHttpUrlAttribute` accepts null or whitespace, or an absolute http(s) URL. It rejects `not-a-url`, `ftp://...` and relative values.
- `MediaPublicUrlOptions.ImageProxyEndpoint` is a new `[OptionalHttpUrl]` property that defaults to an empty string.
- `PhotoUrlsDto` (in Contract, marked `#nullable enable`) has init-only string properties and two factories: `Create(string?, string?, string, string?)` and `Create(MediaPublicUrlOptions, string, string?)`.
- `public PhotoUrlsDto? Urls { get; init; }` was added after `FullUrl` in `PhotoMetadataDto` and `ShortPhotoMetadataDto`. `git diff` on Contract shows additions only: `+1`, `+1`, and `+84` for the new file.

### Task 2: Urls populated at every mapping site
Commits `00f3b590` (RED) and `d2828411` (GREEN).
- **Shops Mapster (2 maps):** `ShopPhoto` and `RoasterPhoto` map to `ShortPhotoMetadataDto` using the string overload. `RoasterDto.CoverPhoto`, `ShortShopDto`, `CoffeeShopDetailsDto` and `CheckInDto` get `Urls` through these maps.
- **Moderation Mapster (3 maps):** `PhotoMetadata` maps to `ShortPhotoMetadataDto` and to `PhotoMetadataDto`, and `ModerationRoasterPhoto` maps to `PhotoMetadataDto`.
- Both configs register `config.NewConfig<PhotoUrlsDto, PhotoUrlsDto>().MapWith(src => src)` as an identity map. This avoids a runtime deep copy and stops `ProjectToType` from calling `Create` once per member.
- **Direct initializers (4):** `GetRoasterByIdHandler` (roaster photos and the linked-shop cover), `ShopMenuDtoFactory` and `MenuDraftMapper`.
- New tests cover:
  - Shops `Adapt` with and without the proxy, for both `ShortShopDto` and `CoffeeShopDetailsDto`
  - the GetRoasterById fallback
  - EF InMemory `CheckInQueries.GetByUserId`, which goes through `ProjectToType`
  - the Moderation review mapping plus Web JSON (`"urls":{`, `"fullscreen":`)

### Task 3: Deploy wiring
Commit `0bc6a93d`.
- **imgproxy service** in `docker-compose.yml`:
  - image `ghcr.io/imgproxy/imgproxy:latest`, depends on minio, no host ports, 512M memory limit
  - `IMGPROXY_BASE_URL` and `IMGPROXY_ALLOWED_SOURCES` are both `http://minio:9000/`
  - `ONLY_PRESETS=true`, with presets `thumb=rs:fill:240:180,card=rs:fill:600:450,detail=rs:fit:1200:1200,full=rs:fit:1920:1920`
  - `AUTO_WEBP` and `AUTO_AVIF` on, `TTL=31536000`
  - no key or salt, so URLs are unsigned
- The `x-media-public-env` anchor now sets `MediaPublicUrlOptions__ImageProxyEndpoint: ${IMAGE_PROXY_URL:-}`. Account, shops, moderation and media all resolve it, as confirmed with `docker compose config`.
- `caddy` now also depends on `imgproxy: condition: service_started`.
- Caddyfile media block: `handle_path /img/*` goes to `reverse_proxy imgproxy:8080`, and a `handle` fallback goes to `minio:9000`. Indentation uses tabs.
- `deploy/.env.example` documents `IMAGE_PROXY_URL=https://media.coffeepeek.by/img`. `dev/dev.env` sets an empty `MediaPublicUrlOptions__ImageProxyEndpoint=`.

### Follow-up fix
Commit `3c8f435f`. My new Moderation test raised a CS8602 warning. I fixed it by capturing `Urls` in a local.

## Verification

- `dotnet build CoffeePeek.slnx --no-incremental` succeeded with 0 errors.
- Required gate projects all passed:

| Test project | Passed / Total |
|---|---|
| CoffeePeek.Shops.Application.Tests | 223/223 |
| CoffeePeek.Shops.Infrastructure.Tests | 47/47 |
| CoffeePeek.ModerationService.Tests | 47/47 |
| CoffeePeek.Moderation.Application.Tests | 13/13 |
| CoffeePeek.ShopsService.Tests | 8/8 |
| CoffeePeek.Account.Infrastructure.Tests | 54/54 |

  ShopsService.Tests and Account.Infrastructure.Tests run `WolverinePreGeneratedHandlersTests`, which shows options validation accepts an absent `ImageProxyEndpoint`.
- Full `dotnet test CoffeePeek.slnx`: 12 assemblies, 1242 passed, 0 failed, 0 skipped. There are **no pre-existing test failures**.
- Task 1 filter `FullyQualifiedName~...Tests.Media`: 36/36 passed.
- Plan grep counts all match: Shops Mapster `PhotoUrlsDto.Create(` = 2, Moderation Mapster = 3, GetRoasterById `Urls = PhotoUrlsDto.Create(` = 2, and ShopMenuDtoFactory and MenuDraftMapper have 1 each.
- All Task 3 grep checks pass. `docker compose -f deploy/docker-compose.yml --env-file deploy/.env.example --profile https config -q` passes, with only the pre-existing `ALLOWED_ORIGINS` unset warning.
- Context7 (`/websites/imgproxy_net`) confirmed:
  - presets-only URL form `/unsafe/p1:p2/plain/...`
  - comma-separated `IMGPROXY_PRESETS`, with options inside one preset separated by `/`
  - plain sources must be percent-encoded (`%`, `?`, `@`)
  - `IMGPROXY_BASE_URL` / `IMGPROXY_ALLOWED_SOURCES` semantics
  - Accept-based AVIF/WebP auto-detection

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CS8602 nullable warning in new Moderation test**
- **Found during:** final gate (warning scan of touched files)
- **Issue:** `dto.Photos[0].Urls!.Detail` followed by `dto.Photos[0].Urls.Thumbnail`. The compiler does not track null state through an indexer access, so the second use warned.
- **Fix:** captured `var urls = dto.Photos[0].Urls;` and asserted on the local.
- **Files modified:** CoffeePeek.ModerationService.Tests/ModerationReviewDtoContractTests.cs
- **Commit:** 3c8f435f

Otherwise the plan was executed as written.

## Notes / Known Warnings

- The two new `PhotoUrlsDto? Urls` lines raise CS8632 in the Contract project, because `<Nullable>disable</Nullable>` is set there. This is intended: the plan says not to add `#nullable` to existing files, and `string? FullUrl` already does the same. The Contract project already has many identical CS8632 warnings.
- Pre-existing warnings in touched files, not caused by this plan and left alone:
  - CS8603 at Shops `MapsterConfiguration.cs:131` (`.Ignore(dest => dest.Menu)`, formerly line 113)
  - CS8603 in `MapsterConfiguration.ModerationShop.cs` (untouched)
- I could not validate the Caddyfile with `caddy adapt` because the Docker Desktop daemon was not running. Only the docker CLI was available, which is enough for `docker compose config` but not for running a container. The change is a standard `handle_path` / `handle` pair with tab indentation.
- The worktree started one commit behind the expected base (`dc3ef43e`). Per the branch check, I reset it to `7fd9f996` before doing any work.

## User Setup Required

To turn on imgproxy variant URLs in production, add `IMAGE_PROXY_URL=https://media.coffeepeek.by/img` to `deploy/.env` on the VPS, then run `docker compose --profile https up -d`. Without it, every variant falls back to the plain MinIO URL.

## Known Stubs

None. `PhotoUrlsDto` properties default to `string.Empty`, but every factory path either sets all four properties or returns `null`.

## TDD Gate Compliance

- Task 1: RED `ded42d59` (`test(...)`, compile failure), then GREEN `ba4b3603` (`feat(...)`).
- Task 2: RED `00f3b590` (`test(...)`, 5 runtime failures, all `Urls` null), then GREEN `d2828411` (`feat(...)`).

## Self-Check: PASSED

- FOUND: CoffeePeek.Shared.Kernel/ImageVariantPresets.cs
- FOUND: CoffeePeek.Shared.Kernel/Options/OptionalHttpUrlAttribute.cs
- FOUND: CoffeePeek.Contract/Dtos/PhotoUrlsDto.cs
- FOUND: CoffeePeek.Shops.Application.Tests/Media/MediaStorageUrlBuilderTests.cs
- FOUND: CoffeePeek.Shops.Application.Tests/Media/PhotoUrlsDtoTests.cs
- FOUND: CoffeePeek.Shops.Application.Tests/Media/MediaPublicUrlOptionsTests.cs
- FOUND commits: ded42d59, ba4b3603, 00f3b590, d2828411, 0bc6a93d, 3c8f435f
