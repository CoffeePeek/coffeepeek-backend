---
phase: 01-public-address-foundation
status: passed-with-infrastructure-gap
verified: 2026-09-27
---

# Phase 1 verification

Foundation implemented; public URL rollout is not implemented or deployed.

| Requirement | Evidence | Result |
|---|---|---|
| Deterministic bounded RU/BY/Latin generation and strict validation | 28 focused domain cases, including newline, reserved, GUID, combining accents and collision suffix | Passed |
| Typed ownership and lifecycle | Canonical/alias restoration, retirement, immutable slug, invalid transitions and UTC/revision checks | Passed |
| Shared per-type namespace for canonical/alias/tombstone | Unfiltered unique Slug indexes in model and generated SQL | Model/SQL verified |
| At most one canonical per owner | Filtered unique OwnerId indexes (`State = 1`) | Model/SQL verified |
| Tombstones retain reservation without dangling FK | Lifecycle CHECK + RESTRICT FK + Retire clears owner | Model/SQL verified; live DB pending |
| Additive schema only | Generated migration + snapshot diff; has-pending-model-changes reports none | Passed |
| Existing supported behavior | Shops domain 173/173 and infrastructure 49/49; no controller/contract changes | Passed |
| Live PostgreSQL uniqueness, FK, concurrency and rollback | No running Docker; sudo startup requires password | Not run; gate before enablement |

## Scope and limitations

Schema does not enforce exactly one canonical on publication, cross-row revision ordering, or immutability against arbitrary SQL. Phase-2 coordinator must supply those invariants. It must demote and promote in one transaction and protect against concurrent allocation/rename/delete. Restrictive FKs intentionally require explicit retirement of every address before owner deletion. Existing unaddressed owners are unaffected.

No production/test database was modified, no migrations applied and no frontend was changed. Migration Down drops reservations; retain populated tables during operational rollback. Existing unrelated package/analyzer warnings are recorded in deferred-items.md. Full feature acceptance awaits lookup/writer integrations, backfill and external frontend redirects.
