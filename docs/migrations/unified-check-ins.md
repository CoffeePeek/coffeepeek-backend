# Canonical check-ins: retained archives and operator backfill

Runtime uses `/api/v1/check-ins` and `/api/v1/moderation/check-ins`. The additive main migrations create canonical tables beside archived Reviews/CheckIns/ModerationReviews and preserve published migration history. They do not themselves copy data. Run the operator command below after applying those schema migrations in a backed-up, isolated rehearsal.

| Source | Canonical mapping |
| --- | --- |
| Reviews | Same UUID in CanonicalCheckIns; unchanged ReviewReports.ReviewId therefore targets the same visit. Header and original metadata remain in source inventory/table. Actual comment, author and frozen drink fields are copied; rating average uses the domain formula (three scores / 3m). |
| CheckIns.ReviewId | Only a unique, author/shop-consistent explicit link merges into its Review UUID. Original VisitedAt and CreatedAtUtc are copied. Unlinked reviews use Reviews.CreatedAtUtc as the documented visit-time fallback. No author/shop/time guessing. |
| ModerationReviews | Exact Reviews.ModerationReviewId link; published content becomes an earlier snapshot when current linked content/photo set differs. Actual current status, rejection reason, moderator and decision timestamp are retained. Unlinked valid submissions become canonical visits under their own UUID. |
| Complete unmatched CheckIns | Private canonical visits under their own UUID. Incomplete rows remain archived with source ID/reason; they are included in retained visit accounting. |
| ReviewHelpfulVotes / ReviewReports | Votes keep target/user/date; HelpfulCount is derived from copied votes, with original cached count audited. Every report field is retained unchanged; no report UPDATE or target rewrite. |
| Historical photos | Exact archived parent/key/owner evidence resolves the unique Media photo UUID. Verified private copies bind that same Media UUID to canonical current photos and immutable revision photos. Original source bytes and associations stay; source anonymous downloads are denied per object. |

Persisted Reviews were created by the old approval consumer, so a valid nondeleted row is publication evidence even without a moderation link. Explicit linked Pending/Rejected/soft-deleted evidence takes precedence. Canonical statuses are translated, not copied as integers. Legacy IsSoftDelete may mean deletion or edit hiding: the manifest records that ambiguity and imports a tombstone so approval replay cannot revive it. Admin-removed content also stays hidden.

Current canonical galleries use only the current revision's evidenced photo set. Older photos remain on the earlier snapshot, including explicitly linked visit photos. Historical sets exceeding five are preserved. Missing Media metadata/bytes, owner or parent conflicts, unknown drink/shop/author, UUID collisions and keys shared with unrelated galleries produce exact unresolved source outcomes. They are release blockers; a saved locator is not proof of protected bytes. Historical non-raster bytes retain MIME metadata and the existing attachment/no-sniff response behavior.

Legacy thumbnail/card/detail/full URLs are generated imgproxy URLs for the same original bucket/key, not independently persisted derivative objects (legacy PhotoMetadata/ShopPhoto and GenerateShopPhotoHandler). The deployed imgproxy has no MinIO credentials and reads originals anonymously, so the per-object anonymous deny also blocks subsequent variant source fetches. Before reopening traffic, invalidate any external proxy/CDN caches of old originals/variants and verify those URLs fail anonymously; previously downloaded client copies cannot be recalled. The current deployment uses plain Caddy reverse_proxy, while IMGPROXY_TTL advertises long-lived cache headers.

## Inputs and commands

Use a protected local directory outside Git for the manifest: it contains full archived rows and account records. Restrict file access and include it in coordinated backups. Do not commit it, credentials or object contents. Load these explicit environment variables from your secret manager:

| Variables | Meaning |
| --- | --- |
| CP_BACKFILL_SHOPS, CP_BACKFILL_MODERATION, CP_BACKFILL_ACCOUNT, CP_BACKFILL_MEDIA | Four separate Npgsql connection strings for the matching service databases |
| CP_BACKFILL_REDIS | Existing service cache connection |
| CP_BACKFILL_MINIO_ENDPOINT, CP_BACKFILL_MINIO_ACCESS_KEY, CP_BACKFILL_MINIO_SECRET_KEY | Privileged existing object-store connection |
| CP_BACKFILL_MINIO_SHOP_BUCKET, CP_BACKFILL_MINIO_USER_BUCKET, CP_BACKFILL_MINIO_PRIVATE_BUCKET | Existing buckets; private bucket must be separate from shop/user buckets |

Dry-run needs only database inputs. Apply/reconcile additionally need object-store inputs; apply needs Redis. From the repository root, after restoring the existing project dependencies:

```powershell
dotnet restore dev/LegacyCheckInsMigration/LegacyCheckInsMigration.csproj -p:MSBuildEnableWorkloadResolver=false
dotnet build dev/LegacyCheckInsMigration/LegacyCheckInsMigration.csproj --no-restore -p:MSBuildEnableWorkloadResolver=false
dotnet run --project dev/LegacyCheckInsMigration --no-build -- dry-run --manifest C:/protected-backfill/reviews-v1.json
dotnet run --project dev/LegacyCheckInsMigration --no-build -- apply --manifest C:/protected-backfill/reviews-v1.json --writers-stopped --queues-drained
dotnet run --project dev/LegacyCheckInsMigration --no-build -- reconcile --manifest C:/protected-backfill/reviews-v1.json
```

Dry-run validates expected legacy/canonical columns and saves an immutable hashed source inventory, deterministic mappings and per-source outcomes without database/object/policy writes. It refuses to replace an existing manifest. Review all IDs/reasons before apply; resolve dry-run blockers before proceeding. Exit codes: 0 successful preparation/reconciliation, 1 stopped on execution/schema/input failure, 2 unresolved reconciliation/release blockers.

Before snapshot/dry-run and throughout apply/reconcile, stop legacy and canonical writers. Drain broker queues with consumers still running, including in-flight deliveries, scheduled work and replayable dead letters; then stop consumers/durability agents. `--writers-stopped --queues-drained` asserts that operational prerequisite. The tool independently refuses unprocessed/scheduled incoming envelopes, outgoing envelopes and dead letters in existing Wolverine stores. Retained inbox rows whose exact status is Handled remain permitted. It never purges messages. A late native creation event would double the replaced account counter, so stopped writers alone are insufficient.

Take coordinated backups of all four databases, objects/versions/tags/bucket policies, inbox/outbox/broker state, binaries/config and the saved manifest. Rehearse first on disposable copies. Do not resume writers until reconciliation and protected/anonymous HTTP checks pass. Keep storage lifecycle/cleanup from expiring retained archived objects.

## Stages, retries and reconciliation

One narrow executable uses existing Npgsql/MinIO/Redis operations, with ordered database sessions and the existing ordered Media claim advisory locks. It adds no service endpoint, automatic startup migration, compatibility API, dual write or new migration tables.

1. Verify source inventory hashes/schema and drain guard. Insert absent canonical visits in private, unapproved staging state; native UUID collisions or changed imports are never overwritten.
2. Commit immutable moderation snapshots in the Moderation database.
3. Verify source/target SHA256 and byte size, mark private objects permanent, deny anonymous reads of exact old shop-bucket objects, then commit trusted Media parent/owner/private-bucket metadata. Commit canonical current and snapshot photo rows with real Media IDs and original order. Sources are not moved/deleted. Shared gallery keys fail closed.
4. Insert absent helpful votes and publish only staged visits whose required photo bindings completed. Interrupted copies/metadata commits can retry the same deterministic objects/IDs; corrupted existing destinations fail instead of being overwritten. A failed photo stage leaves the affected visit private.
5. Under the Account lock, replace CheckInCount with the distinct accounted visit set: all canonical UUIDs plus unmerged retained legacy CheckIns. Original counters stay in the inventory; other account fields/statistics remain. No old creation/approval events are replayed.
6. Complete the saved durable cache-invalidation action with the existing strict Redis invalidator. Pending state is saved before writes; failures keep it pending and retry the same manifest, including shop/rating/search/list/public statistics and owner/profile caches.
7. Reconcile archive/report hashes, original report target identities, every expected visit/snapshot/photo/vote, native canonical rows, all other account fields and exact counters. Verify retained source/private bytes against saved SHA256/size. Reconcile uses existing private-bucket enforcement while reading protected bytes; it does not insert rows or copy/delete objects.

The manifest balances every source visit/submission as mapped, merged, already-present, invalid, conflict or unresolved; photo/vote/report outcomes have their own source IDs and reasons. It includes exact totals and a ReleaseReady flag. Any unresolved review/photo, changed source/native row, missing complaint target, byte mismatch, count mismatch or pending cache action fails release reconciliation. Later user edits cause a conflict instead of being restored to the import. An operator must investigate those outcomes; repeatedly running apply does not overwrite them.

The existing authenticated `POST /api/v1/check-ins/{id}/reports` remains restricted to current approved public visits. Admin detail/removal through `/api/admin/check-in-reports` can inspect and remove the exact canonical target after it becomes hidden/deleted, retaining all complaint audit and retry behavior. Policies, limits, no-store responses and ReviewId storage mapping stay.

Rollback is restore-only: stop writers and restore coordinated database/object/policy/message backups with matching binaries. Do not run destructive Down migrations, truncate canonical data, reset report audit or delete source objects.

## Verification evidence and remaining rehearsal

Offline checks exercise real mapper/parameterized-command construction, count accounting, explicit photo evidence/revision selection, mocked MinIO copy/read callback byte streams, source preservation/hash mismatch/retry failures and real report handlers/MVC host contracts. The guarded PostgreSQL fixture uses minimal typed archives plus production target DDL; it covers staging interruption, mixed native data, votes/reports/counts and replay without overwrites. It excludes photos/Redis and is not a complete historical schema rehearsal.

Source-task result: backend build passed; current CI selection 1,624 passed / 32 live-gate skips / zero failures; separate ModerationService 62 passed; final mapper/command/revision checks 12 passed and guarded PostgreSQL case skipped. Vulnerability audit reported no vulnerable packages.

Actual PostgreSQL execution, full historical schema upgrade/backfill rehearsal, actual MinIO anonymous/signed HTTP protection, Redis fault rehearsal, full gateway HTTP smoke, live apply and deployment are **NOT RUN**. Docker was unavailable and COFFEEPEEK_LIVE_PG unset; no infrastructure was installed or repaired. These remain operator release gates. Passing offline checks does not mean existing deployed records have already moved.
