# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

> Avalonia client and job vacancies are removed from product scope and are no longer part of the platform. Historical entries below that mention them are retained for history only.

### Removed

- Avalonia client references and unused client NuGet packages from the solution
- Job vacancies from product scope (not planned)

## [v0.3.0] - 2026-05-17

### Added

- **Coffee shop suggestion** — client flow with progressive-form UX: required fields (`name`, `address`, `city`) + optional sections expanded on demand.
- **Photo upload for coffee shop suggestions** — presigned upload flow (`api/Photos/shop`) followed by submission to moderation (`api/ModerationShops`).
- **Creating and deleting reviews** on the coffee shop details page: rating form (`place/service/coffee`) + comment; users can delete only their own reviews.
- **Moderator panel** — shown in the header when the JWT contains the `Moderator` role; coffee shop and review queues from the backend, status changes, parsing of the `role` claim from the token, and unit tests.
- **New moderation HTTP client** in the client app with unit tests for ViewModel and WebClient scenarios.
- **CI/CD: Avalonia Browser (WASM) deployment to Vercel** — the workflow publishes `dist/wwwroot` of the Release build; preview on `dev`, production on `main`.

### Changed

- **Home page redesign** — bento-grid layout, updated color scheme and design system (`Resources/Styles`, `Resources/Themes`).
- **Auth-layer error handling** — built-in errors replaced with `DomainException` in all auth handlers (login, logout, register, refresh, confirm email, Google OAuth).
- **Language switching logic** — reworked multi-language support in the client app.
- **AppHost** — Avalonia Desktop/Android excluded from Aspire orchestration; the project was refocused on Browser (WASM).

### Fixed

- Account DB Context migrations updated to match the current schema.
- Fixed Vercel CLI configuration in the CI/CD workflow.
- Removed `appsettings.Development.json` from auth service projects (secrets must not be committed).

## [v0.2.1] - 2026-05-09

### Added

- **Goo Trio loader (02)** — organic three-blob loading animation added to all loading pages: shop catalog, shop detail, and user profile. Blobs oscillate in gold/amber palette using CSS-style Canvas animations.
- **Wobble Ring loader (11)** — compact spinning arc shown inside buttons during async operations. Applied to: Submit Review, Save Profile, and Login buttons. Replaces the generic ProgressBar with a themed organic spinner.
- `Controls/GooTrioLoader` and `Controls/WobbleRingLoader` — reusable Avalonia `UserControl`s for page-level and inline button loading states.
- `Resources/Styles/LoaderStyles.axaml` — shared animation styles for both loaders, registered globally in `App.axaml`.

### Fixed

- `ILocalizationService`, `LocalizationService`, `Loc` singleton, and `ApplyPersistedLanguageExecutor` — missing implementations that caused build failure.
- `GetAllModerationReviewsResultDto.Reviews` renamed to `ReviewDtos`; redundant `[JsonPropertyName]` removed.
- `GetAllModerationShopsResultDto.ModerationShop` and server `GetAllModerationShopsResponse` renamed to `ModerationShops` (plural).
- `ChangeReviewStatusAsync` renamed to `UpdateReviewStatusAsync` for consistent verb usage across the moderation client.
- `ClientSession.SetAccessToken` / `Clear` — made thread-safe with `lock` and `Volatile.Read`.
- `JwtRoleParser.CollectRolesFrom` — uses `HashSet<string>` to deduplicate roles from both claim name variants.
- `ShopDetailViewModelTests` updated to match updated constructor signature (`HttpClient`, `ApiOptions`).

## [v0.2.0] - 2026-04-25

### Added

- Centralized API version management via `ApiVersions.cs`.
- Versioning support via the `X-Api-Version` header.
- New API version `V2.0` for `UsersController` (registration, search, profile).
- Swagger extensions for automatic per-version documentation generation.
- `ClaimsPrincipalExtensions` for safe extraction of `UserId`.
- Implemented the Login flow with JWT and Refresh token support.
- Added client HTTP pipeline and user context infrastructure.
- Added the client app, including Android configuration and connecting the frontend to Aspire.
- Added the coffee shop client: search, city catalog, and real data on the coffee shops page.
- Added the coffee shop details screen with schedule, contacts, reviews, tags, rating, and navigation from the list.
- Added localizations for the coffee shop details page.
- Added MediaService, outbox/messaging integrations, and migrations for the Account, Shops, Moderation, and Media services.

### Changed

- Updated `AuthController`: switched to the new versioning constants. Moved to a REST approach; all endpoints updated.
- Refactored MediatR commands: `LoginRequest` and `RefreshTokenCommand` updated to support the new contracts.
- Optimized coffee shop search and coffee shop details queries.
- The create-review request was removed from its separate flow; the logic moved to coffee shop details `api/coffeeshops/{shopId}`.
- Updated the Equipment model to better support the specialty focus; added domain tests.
- Split DI logic in AccountService; added a persistence layer.
- Authorization check logic moved to the Gateway.
- Shared infrastructure migrated from MediatR/MassTransit to Wolverine where applicable.
- Updated Dockerfiles and the CI/CD pipeline for building services, publishing images, and applying migrations.
- Client loading indicators replaced with shimmer skeleton placeholders.

### Fixed

- Fixed route binding in the Gateway (YARP) to support versioned endpoints.
- Fixed Gateway settings, auth defaults, and auth policy.
- Fixed build issues with Wolverine assemblies, Dockerfiles, and tests.

## [v0.1.2] - 2026-02-02

### Changed

- Equipment model updated to better support specialty; added tests for the domain entity
- Split DI logic in AccountService; added a Persistance layer
- Moved Auth check logic to the Gateway
- MediaService to centralize all photos

## [v0.1.1] - 2026-01-23

### Fixed

- Fixed a bug where a user could log in with a wrong password

## [v0.1.0] - 2026-01-22

### Added

- Started keeping `Changelog.md`
- Initial implementation of microservices: Account, Shops, Moderation (JobVacancies was explored historically; no longer in product scope).
- API Gateway based on YARP.
- .NET Aspire integration for local development.