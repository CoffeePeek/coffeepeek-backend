# Business flow / evidence verification — 2026-10-29

Source: codex/business-evidence, base be74a79b, validated feature tree.
Mobile counterpart: codex/migration-business-flow, base a46715f7.
No deployment or production-account actions were performed.

Plan: additive contract and EF changes; applicant/admin access, private photo
purpose, ownership/size/content checks, retry identity and separation from public
galleries/check-ins. Preserve direct owner PUT, revision and account binding.

Executed unit/contract selections, final results:
- Shops domain: 10 passed.
- Shops application: 24 passed.
- Media: 53 passed.
- Shops HTTP/architecture: 27 passed.
- Shops infrastructure: 2 passed; 13 live PostgreSQL tests skipped.
Total: 116 passed, 13 skipped; no failures. Fake/in-memory results do not certify
live PostgreSQL/MinIO/Gateway acceptance.

The full backend CI solution Debug build passed; final tests rebuilt affected
projects. EF scaffolded both additive migrations; none were applied. Shops
Wolverine codegen write completed; only the changed submission adapter remains.

Retained failures: a DTO constructor argument and init-only ID assignment were
corrected. Codegen first lacked local RabbitMQ options, then rejected an opaque
DI factory; typed registration fixed generation. A sandboxed solution-wide test
attempt could not connect to testhost IPC and was stopped. Relevant tests then
executed successfully with IPC permitted. Existing nullable/analyzer and
unavailable NuGet vulnerability-feed warnings remain.

Open gates: apply migrations and verify local Gateway/Media/PostgreSQL/MinIO
upload-read-approve in a prepared test environment. No production deployment,
native mobile runtime or manual UI acceptance is certified.

## PR #368 integration repair — 2026-10-29

Merged main 5617fd5b into the ownership branch to resolve conflicts with shelf
photo reservations, DI registration and both EF models. Rebased the latest
ownership migration target models on the combined snapshots; existing shelf
migrations remain in order and ownership columns remain additive.

The failing CI upgrade test seeded an old schema through the current EF model,
which attempted to write the not-yet-created OwnershipClaimId column. Main's
historical-column SQL seeds fix this in both variant and backfill upgrade tests;
the repaired tests also assert old photos have no ownership claim after upgrade.

Evidence validation now decodes all pixels instead of trusting readable headers.
The truncated-PNG regression first failed at Assert.False, then passed after the
fix. Shelf claims and evidence attachment reject each other's reservations.

Validation on Windows, Release configuration:
- CI solution build passed with zero errors; existing analyzer warnings remain.
- All 12 .NET test suites passed: 2855 passed, 275 live tests skipped.
- After the final isolation checks, the complete Media suite passed again:
  227 passed, 20 live tests skipped; combined final coverage is 2858 passed.
- Model/snapshot checks in Media and Shops passed without connecting to a DB.
- Full-history secret scan passed: 1143 commits, no leaks.

Docker/PostgreSQL are unavailable locally. PostgreSQL 17 migration, storage and
fault acceptance remains delegated to the PR CI run after publication. Local
skips are not live acceptance. No production data or deployment was changed.

The first repaired CI run (40850386733, 2a1b5456) passed all regular tests,
10 live Media pipeline/upgrade tests and all 10 required Media backfill tests.
It then exposed a missing IOwnershipEvidencePhotos registration in the isolated
BusinessOwnershipLiveTests host: both submit/approve/revoke cases failed during
Wolverine handler generation. Production DI already registered this service.
The test host now supplies a strict test double, and both business kinds submit
photo IDs through IMessageBus, verify attachment to the generated claim ID and
check persisted photo IDs. The local persistence selection passed 250 tests;
the 9 PostgreSQL ownership tests remain skipped locally, pending the new CI run.

CI run 40851508564 (02bae0c0) passed all 9 ownership scenarios, including both
business kinds with evidence IDs. It next failed the historical ownership
upgrade test: that test stopped at AddBusinessOwnershipClaims and queried the
current model before AddOwnershipEvidencePhotos existed. The upgrade now applies
all current additive migrations before EF reads. Its rollback guard regression
also checks that evidence IDs survive the rejected downgrade. The local schema
selection passed 250 tests, with that single live migration test skipped.
