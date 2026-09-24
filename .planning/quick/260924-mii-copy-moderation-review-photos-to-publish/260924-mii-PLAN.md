---
phase: quick-260924-mii
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/ReviewAction.cs
  - CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/IReviewRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/ReviewRepository.cs
  - CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs
  - CoffeePeek.Shops.Infrastructure/Consumers/ModerationReviewApprovedHandler.cs
  - CoffeePeek.Shops.Application.Tests/Features/Review/ModerationReviewApprovedHandlerTests.cs
  - CoffeePeek.Shops.Domain.Tests/Aggregates/ReviewAggregate/ReviewTests.cs
  - deploy/scripts/backfill-review-photos-1-export-moderation.sql
  - deploy/scripts/backfill-review-photos-2-import-shops.sql
autonomous: true
requirements: [QUICK-260924-mii]

must_haves:
  truths:
    - "When a moderation review with photos is approved for the first time, the published Shops review has those photos, in the order they were sent"
    - "When a review is re-approved after an edit, its published photos are replaced by the new set. The old ShopPhotos rows are deleted, not left behind with ReviewId = NULL"
    - "A review that started as a public check-in (CheckinCreatedEvent -> moderation -> approval) gets its photos on the published review"
    - "Reviews approved before this fix can have their photos backfilled by an idempotent manual script that never republishes events and never un-deletes reviews"
  artifacts:
    - path: "CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/ReviewAction.cs"
      provides: "Review.ReplacePhotos returning removed photos"
      contains: "ReplacePhotos"
    - path: "CoffeePeek.Shops.Persistance/Repositories/ReviewRepository.cs"
      provides: "Include(Photos) on GetByModerationReviewId + explicit photo add/remove tracking"
      contains: "Include(x => x.Photos)"
    - path: "deploy/scripts/backfill-review-photos-1-export-moderation.sql"
      provides: "Export of approved moderation review photos to CSV"
    - path: "deploy/scripts/backfill-review-photos-2-import-shops.sql"
      provides: "Idempotent insert into Shops ShopPhotos for reviews that have zero photos"
  key_links:
    - from: "ModerationReviewApprovedHandler.Handle"
      to: "Review.ReplacePhotos"
      via: "reviewDto.Photos mapped with the array index as sortIndex"
      pattern: "ReplacePhotos"
    - from: "ModerationReviewApprovedHandler.Handle (existing path)"
      to: "IReviewRepository.TrackPhotoReplacement"
      via: "explicit RemoveRange(old) + AddRange(new)"
      pattern: "TrackPhotoReplacement"
---

<objective>
Copy the photos from a moderation review onto the published Shops `Review` when it is approved. This covers both new reviews and re-approvals, and check-in-originated reviews take the same path. Also ship a manual backfill for reviews that were already approved before the fix.

Purpose: `ModerationReviewApprovedHandler` ignores `reviewDto.Photos`, so every published review has `photos: []`.
Output: a domain method, a repository and EF wiring, a handler fix, tests, and a two-step psql backfill.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@./CLAUDE.md
@CoffeePeek.Shops.Infrastructure/Consumers/ModerationReviewApprovedHandler.cs
@CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/ReviewAction.cs
@CoffeePeek.Shops.Persistance/Repositories/ReviewRepository.cs
@CoffeePeek.Shops.Application.Tests/Features/Review/ModerationReviewApprovedHandlerTests.cs

<interfaces>
Review (Review.cs): `private readonly List<ShopPhoto> _shopPhotos = [];` and `public IReadOnlyCollection<ShopPhoto> Photos => _shopPhotos.AsReadOnly();`. The field name `_shopPhotos` does NOT match EF's backing-field convention for the property `Photos`.

ShopPhoto (CoffeePeek.Shops.Domain/Entities/ShopPhoto.cs): `public ShopPhoto(string fileName, string contentType, string storageKey, long sizeBytes, Guid ownerId, int sortIndex = 0)`. The Id is set client-side with Guid.NewGuid(). In EF the Id is `ValueGeneratedOnAdd` (unlike RoasterPhoto, which has ValueGeneratedNever).

PhotoMetadataDto (Contract, DO NOT CHANGE): Id, FileName, ContentType, StorageKey, FullUrl?, SizeBytes, OwnerId (Guid).
ModerationReviewDto.Photos: `PhotoMetadataDto[]`, default `[]`.

IReviewRepository has these members: Add, AnyAsync, GetById, GetByModerationReviewId, GetUnlinkedByShopAndUser, SetHelpfulAsync. The only implementation is Shops.Persistance ReviewRepository. The tests use a loose Moq.

Shops "ShopPhotos" columns (from the model snapshot):
- Id uuid PK
- CheckInId uuid NULL
- CoffeeShopId uuid NULL
- ReviewId uuid NULL
- ContentType varchar(30) NOT NULL
- FileName varchar(50) NOT NULL
- StorageKey varchar(200) NOT NULL
- CreatedAtUtc timestamptz NOT NULL
- UpdatedAtUtc timestamptz NULL
- OwnerId uuid NOT NULL
- SizeBytes bigint NOT NULL
- SortIndex int NOT NULL DEFAULT 0

Shops "Reviews" has "Id" and "ModerationReviewId" uuid NULL (unique filtered).

Moderation DB (CoffeeShop.Moderation.Persistence snapshot):
- Table "ModerationReviews": Id, ModerationStatus int (Approved = 1), IsSoftDelete, UserId, ...
- Table "ShopPhotos" (PhotoMetadata): Id, ContentType text, FileName text, StorageKey text, CreatedAtUtc, ModerationReviewId uuid NULL, ModerationShopId uuid NULL, OwnerId uuid NULL, SizeBytes bigint. There is NO sort column.

Deploy: one `postgres` container in deploy/docker-compose.yml. The databases are cpmoderationdb and cpshopsdb. The user is ${POSTGRES_USER}.
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Review.ReplacePhotos + repository/EF wiring + handler fix</name>
  <files>CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/ReviewAction.cs, CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/IReviewRepository.cs, CoffeePeek.Shops.Persistance/Repositories/ReviewRepository.cs, CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs, CoffeePeek.Shops.Infrastructure/Consumers/ModerationReviewApprovedHandler.cs</files>
  <behavior>
    - ReplacePhotos([a, b]) on a review that has [x] leaves Photos == [a, b] in the same order and returns [x]
    - ReplacePhotos([]) clears the photos and returns the previous ones
    - Handler (new review): the added Review has photos whose FileName, ContentType, StorageKey, SizeBytes and OwnerId match the DTO, and whose SortIndex equals the array position 0..n-1
    - Handler (existing review): the photos are replaced and TrackPhotoReplacement(removedOld, addedNew) is called once
  </behavior>
  <action>
1. Domain (ReviewAction.cs): add `public IReadOnlyCollection<ShopPhoto> ReplacePhotos(IEnumerable<ShopPhoto> photos)`.
   - Take a snapshot of the current `_shopPhotos` as a list, clear it, then AddRange the new photos. Materialize the input once.
   - Return the removed snapshot.
   - Add a short comment: the caller must have loaded Photos, or the removed set is incomplete.

2. IReviewRepository: add `void TrackPhotoReplacement(IReadOnlyCollection<ShopPhoto> removed, IReadOnlyCollection<ShopPhoto> added);`

3. ReviewRepository:
   - Implement TrackPhotoReplacement as `dbContext.ShopPhotos.RemoveRange(removed)` plus `dbContext.ShopPhotos.AddRange(added)`.
   - Add a comment explaining why both calls are explicit:
     a. ShopPhoto has three optional parent FKs (CoffeeShopId, CheckInId, ReviewId). Removing an item from the collection only nulls ReviewId, so EF would leave orphan rows. A required/cascade relationship is not possible on a shared table.
     b. ShopPhoto.Id is assigned client-side while EF treats it as ValueGeneratedOnAdd. On a tracked parent, DetectChanges would mark a new photo as Modified, and the UPDATE would hit 0 rows. This is the same bug as the RoasterPhoto 409. Explicit AddRange makes the photos Added, and DetectChanges still fixes up the ReviewId shadow FK from the navigation.
   - This approach deliberately avoids changing the ShopPhoto key config, because that would change the snapshot and need a migration.
   - Change GetByModerationReviewId to `_repository.Include(x => x.Photos).FirstOrDefaultAsync(...)`. Keep the existing "no IsSoftDelete filter" comment.

4. ShopsDbContext, inside `modelBuilder.Entity<Review>`: add `entity.Navigation(r => r.Photos).HasField("_shopPhotos").UsePropertyAccessMode(PropertyAccessMode.Field);`
   - Without it, EF cannot find the backing field for `Photos` (the name differs from `_shopPhotos`), and Include/materialization would try to write into a ReadOnlyCollection.
   - Access mode and backing field are not stored in the model snapshot, so NO migration is needed.
   - If `dotnet ef migrations has-pending-model-changes --project CoffeePeek.Shops.Persistance --startup-project CoffeePeek.ShopsService` can run, use it to confirm there are no pending changes. If it still reports changes, generate one with `make mig-shops n=ReviewPhotosNavigation` and note that in the SUMMARY.

5. Handler:
   - Map `reviewDto.Photos` with `Select((p, i) => new ShopPhoto(p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.OwnerId, i))`, materialized to a list. Use the index because Moderation sends SortIndex = 0 for every photo. This follows CreateShopFromModerationService.cs:111.
   - Existing path: `var removed = existing.ReplacePhotos(photos); reviewRepository.TrackPhotoReplacement(removed, photos);`
   - New path: call `review.ReplacePhotos(photos)` before `reviewRepository.Add(review)`. Add() cascades the graph as Added, so no Track call is needed there.
   - Keep the Handle signature unchanged, so the Wolverine generated code in CoffeePeek.ShopsService/Internal/Generated needs no regeneration. The check-in path goes through this same handler, so it needs no separate code.
  </action>
  <verify>
    <automated>dotnet build CoffeePeek.slnx</automated>
  </verify>
  <done>The solution builds. The handler copies photos in both paths. The repository includes Photos and explicitly removes and adds photo rows. No Contract changes. The migration status is noted.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Handler and domain tests</name>
  <files>CoffeePeek.Shops.Application.Tests/Features/Review/ModerationReviewApprovedHandlerTests.cs, CoffeePeek.Shops.Domain.Tests/Aggregates/ReviewAggregate/ReviewTests.cs</files>
  <behavior>
    - Handle_WhenNoLinkedReviewExists_CopiesPhotosInOrder: the DTO has 3 photos, all with SortIndex 0 from moderation. The added review has 3 photos with SortIndex 0, 1, 2 and matching FileName, ContentType, StorageKey, SizeBytes and OwnerId.
    - Handle_WhenLinkedReviewExists_ReplacesPhotos: the existing review is seeded via ReplacePhotos([old]) and the DTO has [n1, n2]. After Handle, existing.Photos matches the n1 and n2 storage keys, and TrackPhotoReplacement is verified once. Its removed argument contains only old, and its added argument has the 2 new photos.
    - Handle_WhenCheckInOriginatedReviewApproved_PublishesPhotos: a DTO shaped the way the Moderation CheckInCreatedHandler produces it (Header null, photos present, OwnerId = UserId). The added review has the photos.
    - Existing tests stay green. A DTO without photos leads to 0 photos, and TrackPhotoReplacement is called with empty collections, or is harmless either way.
    - ReviewTests: ReplacePhotos replaces the photos, keeps their order, and returns the previous photos.
  </behavior>
  <action>
- Extend BuildEvent with an optional `PhotoMetadataDto[]? photos` parameter, following the existing style: Moq, FluentAssertions, DomainReview alias.
- Before writing the check-in-shaped DTO, read CoffeePeek.Moderation.Application's CheckInCreatedHandler (grep `CheckInCreatedHandler`) so the DTO matches the real shape (Header, UserName).
- Add a ReplacePhotos test to ReviewTests.cs in that file's existing style.
- Avoid xUnit1051 and nullable warnings (see the prior commit f915492c): use the existing `_ct` field and the `!` operator only after a null assertion.
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.Shops.Application.Tests --filter "FullyQualifiedName~ModerationReviewApprovedHandlerTests" && dotnet test CoffeePeek.Shops.Domain.Tests --filter "FullyQualifiedName~ReviewTests"</automated>
  </verify>
  <done>The 3 new handler tests and 1 domain test pass, and the existing tests in both projects still pass.</done>
</task>

<task type="auto">
  <name>Task 3: Idempotent two-step psql backfill for already-approved reviews</name>
  <files>deploy/scripts/backfill-review-photos-1-export-moderation.sql, deploy/scripts/backfill-review-photos-2-import-shops.sql</files>
  <action>
Do NOT republish events. The Shops upsert calls Restore(), which would bring back reviews that users deleted. Plain SQL only, no extensions (no dblink).

Header comment in each file:
- Purpose, and that the script is idempotent and safe to re-run.
- The exact run commands, from the deploy/ directory:
  - Step 1: `docker compose exec -T postgres psql -U "$POSTGRES_USER" -d cpmoderationdb -v ON_ERROR_STOP=1 -f - < scripts/backfill-review-photos-1-export-moderation.sql`
  - Step 2: `docker compose exec -T postgres psql -U "$POSTGRES_USER" -d cpshopsdb -v ON_ERROR_STOP=1 -f - < scripts/backfill-review-photos-2-import-shops.sql`
- Note that the CSV lives at /tmp/review_photos_backfill.csv inside the postgres container. psql runs there, so `\copy` reads and writes the container filesystem. Suggest removing the file afterwards.
- Local Aspire variant: psql with the connection string, same files.

File 1 (moderation DB):
- One `\copy` of a single-line SELECT to '/tmp/review_photos_backfill.csv' WITH CSV HEADER.
- Columns: p."ModerationReviewId", p."FileName", p."ContentType", p."StorageKey", p."SizeBytes", COALESCE(p."OwnerId", r."UserId") AS owner_id, and (ROW_NUMBER() OVER (PARTITION BY p."ModerationReviewId" ORDER BY p."CreatedAtUtc", p."Id") - 1) AS sort_index.
- FROM "ShopPhotos" p JOIN "ModerationReviews" r ON r."Id" = p."ModerationReviewId" WHERE r."ModerationStatus" = 1.
- `\copy` requires the query on one line.
- After it, a SELECT count(*) with the same filter, so the operator sees how many rows were exported.

File 2 (shops DB):
- BEGIN.
- CREATE TEMP TABLE review_photo_import with columns: moderation_review_id uuid, file_name text, content_type text, storage_key text, size_bytes bigint, owner_id uuid, sort_index int.
- `\copy review_photo_import FROM '/tmp/review_photos_backfill.csv' WITH CSV HEADER`.
- A report SELECT of the rows that would be skipped because they exceed the Shops column limits (length(file_name) > 50, content_type > 30, storage_key > 200).
- INSERT INTO "ShopPhotos" ("Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId", "SortIndex", "ReviewId", "CreatedAtUtc", "UpdatedAtUtc") SELECT gen_random_uuid(), i.file_name, i.content_type, i.storage_key, i.size_bytes, i.owner_id, i.sort_index, rv."Id", now(), NULL.
  - FROM review_photo_import i JOIN "Reviews" rv ON rv."ModerationReviewId" = i.moderation_review_id
  - WHERE NOT EXISTS (SELECT 1 FROM "ShopPhotos" sp WHERE sp."ReviewId" = rv."Id"). This gives idempotency: only reviews with zero photos are filled.
  - AND the three length limits hold.
- gen_random_uuid() is built into PG13+ (PG17 here).
- Soft-deleted Shops reviews are filled too (harmless, invisible), but their IsSoftDelete is NOT touched.
- A final SELECT of the inserted count via GET DIAGNOSTICS, or a count of reviews with photos, then COMMIT.
- Quote every identifier exactly as in the snapshots (PascalCase in double quotes).
  </action>
  <verify>
    <automated>grep -c '\\copy' deploy/scripts/backfill-review-photos-1-export-moderation.sql deploy/scripts/backfill-review-photos-2-import-shops.sql && grep -q 'NOT EXISTS' deploy/scripts/backfill-review-photos-2-import-shops.sql && grep -q '"ModerationStatus" = 1' deploy/scripts/backfill-review-photos-1-export-moderation.sql</automated>
  </verify>
  <done>Both scripts exist and have run commands in their headers. The export covers only approved reviews and uses the row number as the order. The import is idempotent (only reviews with zero photos), fills every NOT NULL column, respects the length limits, and never changes IsSoftDelete. If a local Postgres is available, optionally dry-run step 2 with ROLLBACK in place of COMMIT.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| Moderation -> Shops (RabbitMQ) | The approved review DTO, including photo metadata, crosses the service boundary |
| Operator -> prod DB | The manual backfill SQL runs against production databases |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-mii-01 | Tampering | ModerationReviewApprovedHandler photos | accept | The photos were already verified against Media before they were attached in moderation (quick-260924-jpd). Shops trusts the internal bus. |
| T-mii-02 | Tampering | Backfill re-run | mitigate | NOT EXISTS on ReviewId, so a re-run inserts nothing. The whole import runs in one transaction with ON_ERROR_STOP. |
| T-mii-03 | Elevation | Resurrecting user-deleted reviews | mitigate | No event republish. The SQL never updates "Reviews"."IsSoftDelete". |
| T-mii-04 | Denial of Service | Over-long metadata failing the insert | mitigate | The length filters are reported and the offending rows are skipped in the backfill. |
</threat_model>

<verification>
- `dotnet build CoffeePeek.slnx` succeeds
- `dotnet test CoffeePeek.Shops.Application.Tests` and `dotnet test CoffeePeek.Shops.Domain.Tests` pass
- `git diff --stat CoffeePeek.Contract` is empty
- No new migration, unless has-pending-model-changes requires one (note it in the SUMMARY)
</verification>

<success_criteria>
- Published reviews, whether new, re-approved, or check-in-originated, have their moderation photos, in order
- A re-approval replaces the photos without leaving orphan ShopPhotos rows
- The backfill scripts are ready to run, idempotent, and never un-delete reviews
</success_criteria>

<output>
Create `.planning/quick/260924-mii-copy-moderation-review-photos-to-publish/260924-mii-SUMMARY.md` when done
</output>
