# Roaster Contribution — Specification

**Created:** 2026-09-09
**Ambiguity score:** 0.16 (gate: ≤ 0.20)
**Requirements:** 6 locked

## Goal

`Roaster` (currently a name-only catalog tag) becomes a full profile — geocoded address, contact (Instagram/website), photos, about — that any authenticated user can propose via a moderation queue, that a Moderator/Admin approves or rejects, and that anyone can view through a public detail endpoint served by the Shops service.

## Background

Today `Roaster` (`CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Roaster.cs`) has only `Name` and a `CoffeeShops` collection. It's entirely admin-managed via `AdminRoastersController` / `AdminRoasterHandlers` (create/update/delete, name only). Coffee shops link to *existing* roasters by id during their own moderation submission (`ModerationShopRoaster` is just a join row) — there is no way for a regular user to propose a brand-new roaster, and no public endpoint to view one roaster's full profile.

The shop-suggestion pipeline (`ModerationShop` → `ModerationShopCreationService` → `ModerationShopApprovedEvent` → `CreateShopFromModerationService`) is the direct precedent this phase mirrors: submit → geocode → moderator approve/reject → cross-service event → publish into the real catalog.

## Requirements

1. **Roaster domain model expansion** (Shops domain + persistence)
   - Current: `Roaster` has only `Name`; no location, contact, photos, or about text.
   - Target: `Roaster` gains a `Location` (reusing the existing `Location` value object — `CityId`, `Address`, geocoded `Latitude`/`Longitude`, `IsAddressValidated`), a `RoasterContact` value object (`InstagramLink`, `SiteLink`, both optional), an optional `About` text, and a `RoasterPhoto` collection (mirrors `ShopPhoto`: `FileName`, `ContentType`, `StorageKey`, `SizeBytes`, `SortIndex`).
   - Acceptance: EF Core migration on the Shops DB adds these as nullable/empty-by-default; existing name-only `Roaster` rows load and save without error; domain unit tests cover constructing/updating a `Roaster` with the new fields.

2. **User roaster submission endpoint** (Moderation service)
   - Current: No user-facing way to propose a new roaster.
   - Target: `POST /api/ModerationRoasters` (`[Authorize]`, any authenticated user, rate-limited under the existing `moderation-submission` policy — 15 req/min/IP) accepts `Name`, `Address`, `CityId`, `About?`, `InstagramLink?`, `SiteLink?`, `Photos?`. Creates a `ModerationRoaster` aggregate (new, mirrors `ModerationShop`) with `ModerationStatus.Pending`, geocoding the address the same way `SendCoffeeShopToModerationCommand` does.
   - Acceptance: Valid payload returns `201` with the new entity id. Submitting a `Name` matching an existing **approved** roaster (case-insensitive) returns `409`. Unauthenticated request returns `401`.

3. **Admin review of roaster submissions** (Moderation service)
   - Current: No moderation queue/review surface for roasters.
   - Target: `GET /api/ModerationRoasters` (paginated list, `RoleConsts.Moderator`), `GET /api/ModerationRoasters/{id}`, `PUT /api/ModerationRoasters/{id}/status` (`Approved`/`Rejected`) — mirrors `UpdateModerationCoffeeShopStatusHandler`: approve requires a geocoded/validated address, reject requires a non-blank reason, re-approving an already-approved entry is a no-op (no duplicate event). Publishes `ModerationRoasterApprovedEvent` on approval.
   - Acceptance: Approving a submission with an unvalidated address returns `400`. Rejecting without a reason returns `400`. Approving a valid pending submission returns `200` and publishes the event exactly once; calling approve again is a no-op.

4. **Publish approved roaster into the Shops catalog**
   - Current: No consumer exists for a roaster-approval event.
   - Target: Shops service subscribes to `ModerationRoasterApprovedEvent` (new consumer, mirrors `ModerationShopApproveHandler`) and creates a real `Roaster` with all submitted fields (name, location, contact, about, photos). Idempotent by moderation id — replaying the event does not create a duplicate.
   - Acceptance: Publishing the event for a new moderation id creates exactly one `Roaster` row with every submitted field populated. Publishing the same event again does not create a second row.

5. **Admin CRUD extended to full profile fields**
   - Current: `AdminRoasterHandlers` create/update only accept `Name`.
   - Target: `CreateRoasterCommand` / `UpdateRoasterCommand` (and `AdminRoastersController`) accept and persist `Address`, `CityId`, `About`, `InstagramLink`, `SiteLink`, `Photos` alongside `Name`, so admins can manage full profiles directly (not just via the moderation queue).
   - Acceptance: `POST /api/admin/roasters` and `PATCH /api/admin/roasters/{id}` accept and persist the new fields. Existing name-only admin CRUD tests keep passing unmodified.

6. **Public roaster detail endpoint**
   - Current: No public endpoint returns one roaster's full profile — `GetAllRoastersHandler` only returns `{id, name}` pairs for catalog pickers.
   - Target: `GET /api/roasters/{id}` in the Shops service (public, no auth required) returns `RoasterDetailsDto`: name, about, location, contact, photos, plus a short list (`id`, `name`) of coffee shops that reference this roaster (reverse of `CoffeeShop.Roasters`).
   - Acceptance: Requesting an existing roaster id returns `200` with all fields populated and a linked-shops list matching that roaster's actual `CoffeeShop` references. Requesting a nonexistent id returns `404`.

## Boundaries

**In scope:**
- Expanding the `Roaster` domain entity with location, contact, photos, about (Requirement 1)
- User-facing submission endpoint + `ModerationRoaster` aggregate + admin approve/reject endpoints (Requirements 2–3)
- Cross-service publish-on-approval consumer in the Shops service (Requirement 4)
- Extending existing admin Roaster CRUD to the same fields (Requirement 5)
- One new public `GET /api/roasters/{id}` detail endpoint, including linked shops (Requirement 6)

**Out of scope:**
- Roaster search/listing/map view beyond the existing `{id, name}` catalog picker — only detail-by-id is added; a browsable roaster directory is separate future work
- Editing or resubmitting a rejected roaster submission — no resubmission flow; user submits fresh, same as the Shop Issue Reports precedent
- Roaster "ownership"/claiming (a roaster's own account managing its profile) — admin/moderator-managed only after publish
- Fuzzy/near-duplicate name matching — only an exact case-insensitive name conflict returns `409`
- Any change to how a coffee shop selects/links existing roasters (`CoffeeShop.SetRoasters`, `ModerationShopRoaster`) — unaffected by this phase

## Constraints

- Reuse the existing geocoding pattern used by `SendCoffeeShopToModerationCommand` / `ModerationLocation` — no new geocoding provider.
- Reuse the existing photo-upload flow (Media service upload → `UploadedPhotoDto` with a storage key handed to the submission) — same as shop photos, no new upload mechanism.
- New submit endpoint uses the existing `moderation-submission` rate-limit policy (15 req/min/IP).
- All admin/review endpoints gated by `RoleConsts.Moderator`, matching every other moderation controller.
- Schema changes via EF Core migrations + Makefile targets (`mig-shops`, `mig-mod`) — no manual SQL.
- Additive-only: `RoasterDto` / `GetAllRoastersResponse` used by existing shop-creation flows must not break — new fields are additions, not replacements.

## Acceptance Criteria

- [ ] EF Core migrations for the Shops and Moderation DBs add the new Roaster fields without breaking existing rows
- [ ] `POST /api/ModerationRoasters` creates a pending submission and returns `201` with an entity id
- [ ] Duplicate name (case-insensitive) against an approved roaster returns `409` on submission
- [ ] `PUT /api/ModerationRoasters/{id}/status`: approve blocks on unvalidated address (`400`); reject without a reason returns `400`
- [ ] Approving a submission publishes `ModerationRoasterApprovedEvent` and idempotently creates exactly one `Roaster` row in Shops with all fields populated
- [ ] `POST`/`PATCH /api/admin/roasters` accept and persist address, contact, about, photos
- [ ] `GET /api/roasters/{id}` returns the full profile + linked-shops list (`200`) or `404` if the roaster doesn't exist
- [ ] Existing `GetAllRoasters`, shop-creation `SetRoasters`, and admin CRUD name-only tests continue to pass unmodified

## Ambiguity Report

| Dimension          | Score | Min  | Status | Notes                                                        |
|---------------------|-------|------|--------|---------------------------------------------------------------|
| Goal Clarity        | 0.90  | 0.75 | ✓      | Field list, flow, and owning service all confirmed            |
| Boundary Clarity    | 0.85  | 0.70 | ✓      | Explicit out-of-scope list, all boundary questions resolved   |
| Constraint Clarity  | 0.75  | 0.65 | ✓      | Geocoding/photo/rate-limit reuse confirmed against precedent  |
| Acceptance Criteria | 0.80  | 0.70 | ✓      | 8 pass/fail criteria, all testable                             |
| **Ambiguity**       | 0.16  | ≤0.20| ✓      |                                                                 |

## Interview Log

| Round | Perspective | Question summary | Decision locked |
|-------|-------------|-------------------|------------------|
| 0 | Researcher (scouting) | What exists today for Roaster/moderation? | `Roaster` is name-only + admin CRUD; `ModerationShop` pipeline is the reusable precedent (submit → geocode → approve → cross-service event → publish) |
| 1 | Boundary Keeper / Simplifier | Geocode the roaster address like shops, or free text? | Geocoded, reusing the existing `Location` value object and validation gate on approval |
| 1 | Boundary Keeper | Full `ShopContactDto` shape or narrower? | Narrower — Instagram + website link only |
| 1 | Boundary Keeper | Should the detail endpoint list shops carrying this roaster? | Yes — reverse of `CoffeeShop.Roasters` |
| 2 | Failure Analyst | What happens on duplicate submitted name? | Reject with `409`, matching existing admin `Create` conflict behavior |
| 2 | Failure Analyst / Simplifier | Are photos required on submit? Does admin CRUD get the new fields too? | Photos optional; admin CRUD extended to the same full-profile fields |

---

*Spec created: 2026-09-09*
*Next step: proceed to implementation planning (no ROADMAP.md phase exists in this repo — specs here are consumed directly by the plan/execute workflow instead of `/gsd:discuss-phase`).*
