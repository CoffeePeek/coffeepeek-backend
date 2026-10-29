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
