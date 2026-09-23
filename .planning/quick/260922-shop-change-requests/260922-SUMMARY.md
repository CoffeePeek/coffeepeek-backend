---
phase: quick-shop-change-requests
plan: 01
subsystem: moderation-shops
tags: [moderation, coffee-shops, wolverine, ef-core, postgres, photos, menu]
requires: []
provides:
  - Section-specific user change requests for published coffee shops
  - Moderator editing, approval, rejection, and audit trail
  - Idempotent application of approved changes in Shops
affects: [frontend, moderation-ui, coffee-shop-profile]
tech-stack:
  added: []
  patterns: [typed-jsonb-payload, immutable-approval-event, idempotent-consumer-marker]
key-files:
  created:
    - CoffeePeek.Moderation.Domain/Aggregates/ShopChangeRequestAggregate/ShopChangeRequest.cs
    - CoffeePeek.ModerationService/Controllers/ShopChangeRequestsController.cs
    - CoffeePeek.Shops.Infrastructure/Consumers/ShopChangeRequestApprovedHandler.cs
    - CoffeeShop.Moderation.Persistence/Migrations/20260922070415_AddShopChangeRequests.cs
    - CoffeePeek.Shops.Persistance/Migrations/20260922070423_AddAppliedShopChangeRequests.cs
  modified:
    - CoffeePeek.Gateway/appsettings.json
    - CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs
    - CoffeeShop.Moderation.Persistence/Configuration/ModerationDbContext.cs
key-decisions:
  - "One request changes exactly one coffee-shop section."
  - "The versioned typed proposal is stored as jsonb in Moderation."
  - "Approval publishes once; Shops records the request ID before accepting redelivery."
requirements-completed: []
completed: 2026-09-22
---

# Shop Change Requests Summary

Authenticated users can now propose changes to every requested coffee-shop section, while moderators
and admins can correct a pending proposal, approve or reject it, and have an approved change applied
to the published shop.

## Accomplishments

- Added requests for description, contacts, gallery photos, tags, roasters, equipment, brew methods,
  and menu items plus menu photos.
- Added submitter-owned read/list APIs and moderator/admin queue, detail, edit, approve, and reject APIs.
- Added strict single-section payload validation, moderation audit entries, and immutable decisions.
- Added an approval event and an idempotent Shops consumer that applies the selected section and
  invalidates public shop caches.
- Gallery and menu proposals retain existing photo IDs and carry metadata for new uploads, supporting
  removal and ordering without re-uploading retained photos.
- Added Moderation and Shops migrations for requests and applied-request idempotency markers.

## API

- `POST /api/ShopChangeRequests` - submit a pending request.
- `GET /api/ShopChangeRequests/mine` - list the current user's requests.
- `GET /api/ShopChangeRequests/{id}` - read an owned request or any request as moderator/admin.
- `GET /api/ShopChangeRequests` - filtered moderator/admin queue.
- `PUT /api/ShopChangeRequests/{id}` - moderator/admin correction of a pending proposal.
- `PUT /api/ShopChangeRequests/{id}/status` - approve or reject a pending proposal.

## Verification

- `dotnet build CoffeePeek.Backend.ci.slnf -c Debug --no-restore -p:NuGetAudit=false` passes with
  zero errors.
- 1,029 tests pass across the CI solution and the Gateway, MediaService, ModerationService,
  Shops Infrastructure, and ShopsService suites.
- Consumer tests cover all eight sections plus duplicate-event idempotency.
- EF pending-model checks report no changes after both generated migrations.
- `git diff --check` passes; only line-ending notices are emitted by Git on Windows.

## Deviations and Issues

- The repository did not contain the full GSD milestone metadata, so this work used the existing
  quick-task planning format.
- Full gallery coverage exposed and fixed explicit EF state handling for added and removed photos.
- Existing NuGet audit warnings for `Microsoft.OpenApi` and `OpenTelemetry.Api` remain outside this
  feature.

## Deployment

Apply the new Moderation and Shops database migrations before deploying the updated services.

