# Public addresses

This backend slice adds readable addresses for coffee shops, roasters, cities, coffee zones and user profiles without changing existing GUID routes or DTOs. The one intended exception, independent of the flag, is that `GET api/Users/{id}` no longer returns deleted or blocked users (see [User profiles](#user-profiles)). It is disabled by default and has not been run against production data.

## Deployment prerequisite (independent of the flag)

Apply the Shops migrations `20260927112559_AddPublicAddressRegistries`, `20260928072406_AddPublicAddressAudit` and `20260928082712_AddCityAndZonePublicAddresses` **before** this build starts serving traffic, even with `PublicAddresses__Enabled=false`. Deleting a coffee shop, roaster or city always retires its registry rows, so without these tables those deletes fail with 500. `deploy/scripts/ci-deploy.sh` and `deploy/scripts/deploy.sh` apply migrations before restarting; do not deploy with `SKIP_MIGRATIONS=1`. `deploy/scripts/update.sh` restarts without migrating, so run `deploy/scripts/apply-migrations.sh` first when using it.

AccountService has the same requirement. Apply the Account migration `20260928134755_AddUserPublicAddresses` to `cpaccountdb` **before** this AccountService build serves traffic, even with `PublicAddresses__Enabled=false`. Account deletion (self-service confirmation and admin delete) always retires the user's addresses, so without the `UserPublicAddresses` table those requests fail with 500. `deploy/scripts/apply-migrations.sh` migrates Account first, before Shops and Moderation. The script rules above apply unchanged: no `SKIP_MIGRATIONS=1`, and run `apply-migrations.sh` before `update.sh`.

## Enablement gate

The flag is per service. `PublicAddresses__Enabled=true` must be set on AccountService separately from ShopsService, and each service has its own gate below. It is currently unset (false) for both services.

1. Confirm the deployment prerequisite migrations above (Shops and Account) are applied in the target environment.
2. Run the live PostgreSQL suite described in [Live PostgreSQL gate](#live-postgresql-gate) against a non-production server. It covers uniqueness, restrictive foreign keys, concurrent allocation/rename and transaction/savepoint rollback. Cities and coffee zones add the checks below. The automated gate passed on 2026-09-30; see [Live gate evidence](#live-gate-evidence). Re-run it for changes to the address implementation or migrations.
   - The unique `Slug` index and the filtered canonical owner index on `CityPublicAddresses` and `CoffeeZonePublicAddresses` hold under concurrent allocation. Two concurrent "Минск" creations serialize on the advisory lock.
   - `FK_CityPublicAddresses_Cities_OwnerId` and `FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId` are RESTRICT: deleting a city without retiring its addresses fails.
   - Deleting a city that still has coffee zones returns 409, and its address retirement rolls back.
   - `CK_PublicAddressAudit_Kind` accepts kinds 3 (city) and 4 (coffee zone).
   - `CK_CityPublicAddresses_Lifecycle` and `CK_CoffeeZonePublicAddresses_Lifecycle` reject a canonical row without an owner.

   User profiles (AccountService, `cpaccountdb`) add the checks below. These automated checks also passed in the recorded [Live gate evidence](#live-gate-evidence).
   - The unique `Slug` index and the filtered canonical owner index `IX_UserPublicAddresses_OwnerId` on `UserPublicAddresses` hold under concurrent registrations. `Ivan`, `ivan` and `Иван` registering at once get distinct slugs, serialized by advisory lock `7402100401`.
   - The rename demotion flush never violates `IX_UserPublicAddresses_OwnerId`.
   - `FK_UserPublicAddresses_Users_OwnerId` is RESTRICT: a physical user delete without retirement fails.
   - `CK_UserPublicAddresses_Lifecycle`, `CK_UserPublicAddresses_Slug`, `CK_UserPublicAddresses_Revision` and `CK_UserPublicAddresses_Timestamps` reject invalid rows.
   - Savepoint rollback inside Wolverine's transaction works:
     - a username unique violation leaves the address unchanged;
     - a duplicate-email registration leaves no address row;
     - the Google concurrent-creation retry still succeeds after `ChangeTracker.Clear()`.
   - The user backfill cursor query translates and pages correctly on PostgreSQL, and a concurrent unique violation during write-mode backfill returns 409.
   - The migration `20260928134755_AddUserPublicAddresses` applies cleanly to `cpaccountdb`.
   - An HTTP smoke run through the gateway has not been done either. It is manual and not part of the live suite; follow [HTTP smoke (manual)](#http-smoke-manual). It should show that `/api/Users/by-slug/reviews` reaches AccountService, that `ids=` query arrays bind and that hidden profiles return the identical 404 body. Only the ASP.NET Core routing precedence API proves the route order today.
3. Set `PublicAddresses__Enabled=true` for ShopsService and restart it.
4. Run dry-run backfill pages and review collisions before running write mode. Follow [Dry-run backfill review](#dry-run-backfill-review) page by page.
5. Before enabling AccountService, decide on slug reservation growth (D-02). Each username change that produces a new slug reserves one more slug permanently: the previous slug stays an alias, and every slug of the user becomes a tombstone on deletion. Today only the `auth-endpoints` rate limit bounds how fast one account can reserve slugs by changing its username repeatedly. Either accept this growth or add a username-change cooldown first. No cooldown exists in this build.
   Also decide on lock contention. Account deletion (self-service and admin) takes the profile-address advisory lock `7402100401` and holds it until Wolverine commits, which is after the Redis token revocation and SignalR logout calls. With the flag on, every registration, Google sign-up, username change and write-mode backfill waits behind a slow deletion. Measure deletion latency on the target environment, or move those network calls after commit, before enabling.
6. Set `PublicAddresses__Enabled=true` for AccountService and restart it. Then run the user backfill with the step 4 rules (see [Bounded backfill](#bounded-backfill)).

Do not enable writers or execute write-mode backfill for a service before steps 1–2 pass for that service. Do not enable AccountService before step 5 is decided. When disabled (and the prerequisite migrations are applied), new endpoints return 503 and new entities are not assigned addresses; existing GUID APIs are unchanged.

## Live PostgreSQL gate

This is the automated part of step 2. It runs only against a non-production server that you provide, and it never touches deployed configuration.

### What the suite covers

The live suite is an opt-in integration suite in `CoffeePeek.Shops.Infrastructure.Tests` and `CoffeePeek.Account.Infrastructure.Tests`, tagged with the trait `Category=LivePostgres`. Each run creates its own throwaway databases, applies the real EF Core migrations to them and drops them afterwards. There is no Docker dependency. With `COFFEEPEEK_LIVE_PG` unset, every live test reports Skipped, so CI and `dotnet test CoffeePeek.slnx` stay green.

| Project | Step 2 gate item | Test class | Gate IDs |
| --- | --- | --- | --- |
| Shops | Server prerequisites, the throwaway database lifecycle and the lock-wait probes that the other live tests rely on | `LivePostgresHarnessTests` | harness |
| Shops | The Shops migrations apply cleanly to an empty database | `ShopsLiveSchemaTests` | G-S0 |
| Shops | The unique `Slug` index and the filtered canonical owner index on every registry; `CK_*_Slug`, `CK_*_Lifecycle`, `CK_*_Revision` and `CK_*_Timestamps`; `CK_PublicAddressAudit_Kind` accepts kinds 3 and 4 | `ShopsLiveSchemaTests` | G-S1 |
| Shops | RESTRICT foreign keys on all four registries, including `FK_CityPublicAddresses_Cities_OwnerId` and `FK_CoffeeZonePublicAddresses_CoffeeZones_OwnerId`; a supported delete retires the addresses before it deletes the owner | `ShopsLiveSchemaTests` | G-S2 |
| Shops | Two concurrent "Минск" creations serialize on advisory lock `7402100101`; concurrent renames and allocations; the rename A->B->A demotion flush; a failed rename rolls back both steps | `ShopsLiveConcurrencyTests` | G-S3, G-S4, G-S5, G-S6 |
| Shops | Savepoint rollback inside an ambient transaction; deleting a city that still has coffee zones returns 409 and its address retirement rolls back; zone publish, archive and republish with the real `Polygon` | `ShopsLiveTransactionTests` | G-S7, G-S8, G-S9 |
| Shops | The migrations apply incrementally over legacy data; page-by-page backfill (dry run, cursor, replay, collisions, the cross-page caveat); an in-flight conflict during write-mode backfill returns 409 and rolls back the page; the data-level rollback rehearsal | `ShopsBackfillRehearsalTests` | G-S0 (incremental), G-S10, G-S10b, G-S11 |
| Account | `20260928134755_AddUserPublicAddresses` applies cleanly; `CK_UserPublicAddresses_*`, `IX_UserPublicAddresses_Slug` and `IX_UserPublicAddresses_OwnerId`; `FK_UserPublicAddresses_Users_OwnerId` is RESTRICT | `AccountLiveSchemaTests` | G-A0, G-A1, G-A2 |
| Account | `Ivan`, `ivan` and `Иван` registering at once get distinct slugs, serialized by advisory lock `7402100401`; the rename demotion flush never violates `IX_UserPublicAddresses_OwnerId`; a username unique violation leaves the address unchanged | `AccountLiveTransactionTests` | G-A3, G-A4, G-A5 |
| Account | A duplicate-email registration leaves no address row; the Google concurrent-creation retry succeeds after `ChangeTracker.Clear()` | `AccountLiveTransactionTests`, `AccountLiveRegistrationTests` | G-A6, G-A7 |
| Account | The migration applies incrementally over populated `Users`; the user backfill cursor translates and pages correctly; a concurrent unique violation during write-mode backfill returns 409; the data-level rollback rehearsal | `AccountBackfillRehearsalTests` | G-A0 (incremental), G-A8, G-A9 |

The HTTP smoke through the gateway is not part of the suite (see [Not covered](#not-covered)).

### Server and role

- Use a NON-production PostgreSQL 17 or newer server that the test machine can reach.
- `pg_trgm` must be available (standard contrib; the Shops migrations need it), and databases must use UTF8 encoding. The suite checks both and fails with a clear message otherwise.
- The server admin creates a dedicated login role:

  ```sql
  CREATE ROLE cp_live_tests LOGIN CREATEDB NOSUPERUSER NOCREATEROLE NOREPLICATION PASSWORD '<secret>';
  ```

- `pg_hba.conf` must let the role connect to all databases, because each run creates new database names:

  ```text
  host all cp_live_tests <client-ip>/32 scram-sha-256
  ```

- The role must not be a member of any production role. It needs no other grants: it owns the databases it creates, so it can create `pg_trgm` in them and drop only them.

### Credentials (D-02)

- Set `COFFEEPEEK_LIVE_PG` to a connection string without a password:

  ```text
  COFFEEPEEK_LIVE_PG=Host=<host>;Port=5432;Database=postgres;Username=cp_live_tests;SSL Mode=Prefer
  ```

- The database must be exactly `postgres`, the maintenance database. The suite refuses any other database and refuses a `Password` key. Use `SSL Mode=Require` if the server enforces TLS.
- Put the password in `%APPDATA%\postgresql\pgpass.conf`, as the line `<host>:5432:*:cp_live_tests:<secret>`, or in a file named by `PGPASSFILE`. Keep that file outside the repository.
- Set the variables before starting the shell or agent that runs the tests. `setx` affects only new processes, so a shell started earlier does not see the variable and every live test skips.
- Never commit or paste the connection string or the password, including in chat, issues and logs.

### Safety guards (D-03)

- A superuser role is refused unless `COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER=1` is also set (exactly `1`). Use it only for a disposable server, such as a local container.
- Only databases named `cp_live_<service>_<yyyyMMdd>t<HHmmss>_<8hex>` are created or dropped. They are dropped with `DROP DATABASE … WITH (FORCE)`. Any other name is refused before a connection is opened.
- Each throwaway database is dropped when its tests finish, on success and on failure.
- At the start of a run, this role's own `cp_live_*` leftovers older than 6 hours are swept. Newer databases, for example those of a concurrent run, are left alone.
- Every throwaway connection uses `lock_timeout=30s`, so a locking bug fails fast instead of hanging.
- The suite connects only to `postgres` and to its own `cp_live_*` databases. It never connects to the `cp*db` service databases such as `cpshopsdb` and `cpaccountdb`.
- Connection failures are reported without the host.

### Running the suite

Build both test projects in Release, then run each one with the in-process xUnit runner from its `bin/Release/net10.0` directory:

```bash
dotnet build CoffeePeek.Shops.Infrastructure.Tests/CoffeePeek.Shops.Infrastructure.Tests.csproj -c Release
dotnet build CoffeePeek.Account.Infrastructure.Tests/CoffeePeek.Account.Infrastructure.Tests.csproj -c Release
(cd CoffeePeek.Shops.Infrastructure.Tests/bin/Release/net10.0 && dotnet exec CoffeePeek.Shops.Infrastructure.Tests.dll -noColor -trait "Category=LivePostgres" -showLiveOutput)
(cd CoffeePeek.Account.Infrastructure.Tests/bin/Release/net10.0 && dotnet exec CoffeePeek.Account.Infrastructure.Tests.dll -noColor -trait "Category=LivePostgres" -showLiveOutput)
```

On machines where the xUnit test apphost is not blocked, this is equivalent:

```bash
dotnet test CoffeePeek.Shops.Infrastructure.Tests --filter "Category=LivePostgres"
dotnet test CoffeePeek.Account.Infrastructure.Tests --filter "Category=LivePostgres"
```

Share only these lines:

- the summary line of each project: `Total: …, Errors: …, Failed: …, Skipped: …` from the in-process runner, or the `Passed!`/`Failed!` line from `dotnet test`;
- the `LIVE-PG server_version=` line, printed by `LivePostgresHarnessTests` (shown live with `-showLiveOutput`);
- the output of `git rev-parse --short HEAD`.

Share nothing else. Never share the connection string, the host or the password.

### What counts as a pass (D-04)

- Both projects report `Failed: 0` and `Skipped: 0` for the LivePostgres filter, and `Errors: 0` with the in-process runner.
- The passed count equals the number of live tests that the same build reports as skipped when `COFFEEPEEK_LIVE_PG` is unset. Run each project once with the variable unset to get that number.
- A run with the variable unset, a run with any skipped live test, and a run on a blocked test host are never evidence. Skipped looks green but proves nothing.

### Cleanup

A normal run leaves nothing behind. After a crashed or cancelled run, connect to `postgres` as the same role and list the leftovers:

```sql
SELECT datname FROM pg_database WHERE datname LIKE 'cp\_live\_%';
```

Drop each one as the same role:

```sql
DROP DATABASE "<name>" WITH (FORCE);
```

Drop only names that match the `cp_live_<service>_…` pattern. The next run also sweeps this role's leftovers that are older than 6 hours.

### Not covered

- A CI job with a PostgreSQL service container (deferred, D-09). The live suite runs only when an operator provides a server.
- A rehearsal against a production schema dump (deferred, D-10).
- The HTTP smoke through the gateway. It is manual; see [HTTP smoke (manual)](#http-smoke-manual).
- The AccountService enablement decisions in step 5: slug reservation growth (D-02 of the user-profile design), the deletion lock hold and the shared `auth-endpoints` rate-limit bucket.

### HTTP smoke (manual)

This has not been run. Run it in the `dev/` stack, or in a non-production environment that you are authorized to change. `$GW` is the gateway base URL.

- With AccountService's flag off, the slug `reviews` must reach AccountService:

  ```bash
  curl -i "$GW/api/Users/by-slug/reviews"
  ```

  The response must be a 503 ProblemDetails from AccountService. That proves routing: without `account-Users-by-slug-route`, Shops would answer through its `/api/users/{userId}/reviews` route.
- With AccountService's flag on:
  - Unknown and hidden (blocked or deleted) profiles return 404, and the two bodies are identical except `traceId`:

    ```bash
    diff <(curl -sS "$GW/api/Users/by-slug/<unknown-slug>" | jq -S 'del(.traceId)') \
         <(curl -sS "$GW/api/Users/by-slug/<blocked-user-slug>" | jq -S 'del(.traceId)')
    ```

  - `ids=` query arrays bind. This returns the addresses of both visible users:

    ```bash
    curl -i "$GW/api/Users/public-addresses?ids=<guid>&ids=<guid>"
    ```

- With ShopsService's flag on and before any backfill, `curl -i "$GW/api/Catalogs/cities/by-slug/<known>"` returns 404, not 503.

### Live gate evidence

The automated PostgreSQL gate passed on **2026-09-30 (UTC)** on PostgreSQL **17.11 (Debian 17.11-1.pgdg13+2)**. The user authorized a separate disposable PostgreSQL container on the supplied server. It had its own storage, a 512 MiB memory limit, a 0.5 CPU limit and a loopback-only port accessed through an SSH tunnel. No application database was used. The harness left zero `cp_live_*` databases; the container, its anonymous volume and the temporary pgpass file were removed after the run.

Source: base commit `281696be483daa449465ea5a3afefc1d39bec57b` plus the phase-5 test additions in this working tree, implemented directly without GSD. Production source, migrations, packages and deployed flags were unchanged. SHA-256 of the test-source manifest: `576ae385b3a16ed77277bba99cf994065ae158abcb39a564920f93ccc0df5ff8`. The manifest contains all new `Account*.cs` and `Shops*.cs` files under the two `LivePostgres` directories, sorted by repository-relative path, one `path lowercase-sha256` line each, UTF-8 without BOM, LF endings and a final LF.

Runner: xUnit v3 in-process, Release build, `dotnet exec <project>.dll -noColor -parallel none -trait "Category=LivePostgres"`.

```text
CoffeePeek.Shops.Infrastructure.Tests   Total: 24, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0
CoffeePeek.Account.Infrastructure.Tests Total: 15, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0
```

For the same build with `COFFEEPEEK_LIVE_PG` unset, Shops reported `Total: 24, Skipped: 24` and Account reported `Total: 15, Skipped: 15`, both with zero errors, failures and not-run tests. Thus all 39 discovered live checks ran and passed. The CI solution Release build had zero errors; all ten test projects passed their ordinary checks (1,460 passed, 39 opt-in live checks skipped).

`PublicAddresses__Enabled` stays unset for ShopsService and AccountService. This evidence closes the automated database gate only. The manual HTTP/YARP smoke, production migration application, reviewed production backfill and Account enablement decisions remain deployment prerequisites.

## Public API

| Entity | Prefix | `data` | Canonical path |
| --- | --- | --- | --- |
| Coffee shop | `api/CoffeeShops` | Shop details | `/coffee-shops/{slug}` |
| Roaster | `api/roasters` | Roaster details | `/roasters/{slug}` |
| City | `api/Catalogs/cities` | `CityDto` `{id, name}` | `/cities/{slug}` |
| Coffee zone | `api/Catalogs/coffee-zones` | `MapCoffeeZoneDto`, the same shape as `/api/Map` zones | `/coffee-zones/{slug}` |
| User profile | `api/Users` | `PublicUserProfileResponse` `{userName, createdAtUtc, about, avatarUrl, reviewCount, checkInCount, addedShopsCount}`; no email or phone | `/users/{slug}` |

| Method | Path | Result |
| --- | --- | --- |
| GET | `/{prefix}/by-slug/{slug}` | Entity data plus canonical address metadata |
| GET | `/{prefix}/public-addresses?ids={guid}&ids={guid}` | Up to 100 visible canonical addresses |
| GET | `/{prefix}/{id}/public-address` | Address for one legacy GUID |

Canonical and alias lookups both return HTTP 200 JSON. Unknown, hidden and retired owners return 404. Input is matched case-insensitively and metadata always contains the normalized canonical slug.

Every city is public: the same set as `GET api/Catalogs/cities`. A coffee zone is public only while Published. Draft, Archived, unknown and retired zones all return 404 and are omitted from batch results. The zone by-slug endpoint checks the status again when it reads the zone data, so a zone archived between the two reads also returns 404.

```json
{
  "data": { "id": "11111111-1111-1111-1111-111111111111" },
  "address": {
    "entityId": "11111111-1111-1111-1111-111111111111",
    "slug": "surf-coffee-oktyabrskaya-16",
    "canonicalPath": "/coffee-shops/surf-coffee-oktyabrskaya-16",
    "revision": 2,
    "isAlias": true
  }
}
```

City example (`GET api/Catalogs/cities/by-slug/minsk`):

```json
{
  "data": { "id": "22222222-2222-2222-2222-222222222222", "name": "Минск" },
  "address": {
    "entityId": "22222222-2222-2222-2222-222222222222",
    "slug": "minsk",
    "canonicalPath": "/cities/minsk",
    "revision": 1,
    "isAlias": false
  }
}
```

User profile example (`GET api/Users/by-slug/ivan-petrov`, where `ivan-petrov` is an alias left by a username change to `Petr`):

```json
{
  "data": {
    "userName": "Petr",
    "createdAtUtc": "2026-03-14T09:30:00Z",
    "about": "Filter coffee first",
    "avatarUrl": null,
    "reviewCount": 12,
    "checkInCount": 30,
    "addedShopsCount": 2
  },
  "address": {
    "entityId": "33333333-3333-3333-3333-333333333333",
    "slug": "petr",
    "canonicalPath": "/users/petr",
    "revision": 2,
    "isAlias": true
  }
}
```

Gateway routing for user profiles (D-07): the gateway has a dedicated `account-Users-by-slug-route` for `/api/Users/by-slug/{slug}`. Without it, the slug `reviews` would be captured by the Shops `/api/users/{userId}/reviews` route. The literal `by-slug` segment outranks the `{userId}` parameter, so every valid slug reaches AccountService. Batch (`api/Users/public-addresses`) and legacy GUID (`api/Users/{id}/public-address`) lookups use the existing `/api/Users/{**remainder}` route. All three share the `auth-endpoints` limit (20/min per client IP). That is a single bucket per IP that is also used by login, token refresh and every other `/api/Users/**` call, so a client browsing many profiles can make its own token refresh return 429 and log the user out. Revisit this (a separate policy with the same numbers, or a higher limit for lookups) before enabling AccountService and for server-side rendering in phase 6.

`canonicalPath` is a server-built relative page path. The API never redirects JSON and never accepts a redirect target from the request. Existing `GET api/CoffeeShops/{id:guid}` and `GET api/roasters/{id:guid}` contracts remain unchanged. The existing `GET api/Catalogs/cities` list and `GET api/Map` payloads are also unchanged and carry no address metadata.

The new metadata is not stored in the existing shop/roaster detail caches: resolution reads the live registry and visibility from the database, then invokes the unchanged detail query. Therefore address changes require no invalidation of those payload caches.

City and zone address metadata is not stored in the `city:list:all`, map or search caches either. Lookups read the registry and visibility live, and the city by-slug data is read from the database rather than from `city:list:all`, so no cache invalidation is needed.

User address metadata is not cached anywhere, and there is no Redis profile cache. The user endpoints are `NoStore` and read the registry and visibility live, so no cache invalidation is needed.

## Cities and coffee zones

Each kind has one global namespace (cities, coffee zones, shops and roasters are independent, so `minsk` can exist in all four). Zone slugs are unique across all cities, not per city. A same-name zone in another city therefore gets a deterministic short suffix derived from its GUID. Admins can choose a different slug with initialize or rename.

Allocation rules:

- A city gets an address when it is created. A coffee zone gets one on its first publication; drafts get no automatic address.
- Display-name edits never rename an address.
- Archiving a zone or moving it back to Draft hides it without retiring its address. Republishing reuses the same address.
- An admin may initialize a Draft zone's slug before publishing it. Publication keeps that slug.
- Deleting a city retires all its addresses, even when the feature is disabled. Zones are archived, not deleted.

## User profiles

User addresses live in AccountService (`UserPublicAddresses` in `cpaccountdb`). Shops stores no user slugs. Users are a fifth namespace, independent of the four Shops kinds, so `minsk` can also be a user slug.

**Username policy (D-01).**
- An address is allocated when the user is created, by email registration or Google sign-in, from the username.
- A username change is an owner-authorized rename. The previous slug stays an alias of the same user, and changing back (A->B->A) restores it.
- Case- or punctuation-only changes that produce the same slug change nothing.
- Colliding usernames get a `-<8hex>` suffix derived from the user GUID.
- Reserved or unsupported names get `user-<8hex>`. Examples are `admin`, a bare `user`, and names with no Latin letters, supported Cyrillic letters or digits, such as Greek-only or CJK-only names.
- Users created before enablement get an address through backfill or on their next username change.
- With the flag off, nothing is allocated or renamed. A username change made while the flag is off leaves an existing address on the old slug. The address follows only on the next username change made while the flag is on.

**Visibility (D-03).**
- A profile is public if and only if the account is not deleted and not blocked. There is no privacy flag, and users with an unconfirmed email are visible.
- Unknown, retired, deleted and blocked addresses all return an identical 404 with the framework's generic ProblemDetails body (no user data). They are omitted from batch results.
- Blocking hides a profile without retiring it, and unblocking restores the same address.
- The by-slug endpoint checks visibility again when it reads the profile data. A user deleted or blocked between the two reads also gets the identical 404.
- Invalid, reserved or GUID-shaped slugs return 400. Every user address endpoint returns 503 while AccountService's flag is off.

**GUID route fix (D-04).** `GET api/Users/{id}` now returns 404 for deleted and blocked users, whatever the flag says. Visible users get the same DTO shape as before. Consequence for Shops: `AccountUserExistenceLookup` (`CoffeePeek.Shops.Infrastructure/Account/AccountUserExistenceLookup.cs`) treats 404 as "does not exist", so it now means "exists and visible (not deleted, not blocked)". It has no callers today, and no Shops code changed.

**Deletion (D-03, D-06).**
- Deletion always retires every address of the user, even when the feature is disabled.
- Tombstones keep only the reserved slug, with no owner, display name, email or history.
- Account keeps no address audit or history, and no API returns tombstones, so nothing *public* links a deleted user to former slugs.
- Known limit: anyone with database access can still correlate them. All of a user's tombstones share one `Revision` and `UpdatedAtUtc`, written in the same transaction as the soft-deleted `Users` row, and that row keeps the username, so the last slug can be recomputed. Suffixed slugs (`-<8hex>`, `user-<8hex>`) also contain the first 8 hex digits of the user GUID, which Shops review DTOs expose. Treat tombstones as personal data when handling database dumps; full unlinkability would require anonymizing the soft-deleted user row, which is out of scope.

**Disclosure notes.**
- Anyone holding an old link learns the new username through the alias.
- Google users' usernames are derived from the email local part, so their `/users/{slug}` URLs are crawlable (D-09). The external frontend may `noindex` profile pages.

**Author links.** The frontend links review authors with one `GET api/Users/public-addresses?ids=` call (at most 100 ids) per page, using the `userId` from Shops review DTOs.

## Admin API

All endpoints require the Admin policy. `{entity}` is exactly `shops`, `roasters`, `cities` or `coffee-zones`; any other value is rejected. The gateway forwards `/api/admin/cities/**` and `/api/admin/coffee-zones/**` for Moderators, but ShopsService still enforces the Admin policy on these endpoints.

| Method | Path |
| --- | --- |
| POST | `api/admin/{entity}/public-addresses/{id}/initialize` |
| PATCH | `api/admin/{entity}/public-addresses/{id}` |
| GET | `api/admin/{entity}/public-addresses/{id}/history?afterRevision=0&limit=50` |
| POST | `api/admin/{entity}/public-addresses/backfill?afterId={cursor}&limit=50&dryRun=true` |

Initialize/rename body:

```json
{
  "slug": "surf-coffee-center",
  "expectedRevision": 2,
  "reason": "Use the public brand name"
}
```

Initialization requires revision `0`; `slug` may be null to generate it. Rename requires a canonical lowercase slug and the current positive revision. Responses use 400 for invalid input, 404 for an unknown owner and 409 for stale revision or a reserved address. Historical aliases remain owned by the entity and resolve directly to its current canonical address.

Users have exactly one admin endpoint, in AccountService:

| Method | Path |
| --- | --- |
| POST | `api/admin/users/public-addresses/backfill?afterId={cursor}&limit=50&dryRun=true` |

It requires the Admin policy in AccountService and on the gateway `account-admin-route`. There is no initialize, rename or history for users (D-06, D-10): usernames drive renames, and the registry rows are the only history.

## Bounded backfill

Backfill is ordered by owner GUID, accepts 1–100 rows and returns `nextCursor` when another page exists. Always start with `dryRun=true`; it performs no writes and reports each proposed slug, whether a collision suffix was needed and whether the owner was already initialized.

Repeat the same dry-run request if desired, then submit it with `dryRun=false`. A replay never renames initialized owners. Continue with the returned cursor until it is null. Each entity is a separate run. No production backfill is part of this repository change.

City backfill includes every city; coffee zone backfill includes only Published zones. Review the city dry-run output for:

- Duplicate city names. `City.Name` is not unique, so the second city (in GUID order) gets a suffix.
- RU/BY spelling variants, which produce different slugs: `Гродно` -> `grodno` vs `Гродна` -> `grodna`, and `Могилёв` -> `mogilyov` vs `Магілёў` -> `magilyou`.

Where the generated slug is not the one you want, choose a manual slug with initialize before running write mode.

User backfill is a separate run against AccountService, with the same dry-run, cursor and replay semantics. It includes blocked users (allocated but hidden) and excludes soft-deleted users. `hadCollision` is true only when a suffix beyond the stem's first candidate was needed. For an already-initialized user, it compares the existing canonical slug with the first candidate for the current username. Review the dry-run output for transliteration collisions such as `Ivan`/`ivan`/`Иван`, which all produce `ivan`: only one of them gets the bare slug, and the others get a suffix. There is no manual initialize for users, so a slug can be changed only through a username change.

## Dry-run backfill review

This is for authorized operators only, and only after steps 1–3 pass for that service. It is a documentation snippet, not a committed script.

Run it once per entity. `E` is one of `shops`, `roasters`, `cities` or `coffee-zones`. `$GW` is the gateway base URL, and `$TOKEN` holds an admin JWT. Keep the token out of chat and shell history: read it with `read -rs TOKEN` instead of typing it into a command.

Set the entity and an empty cursor once:

```bash
E=cities; CURSOR=""
```

Then run the loop. Each page is dry-run, reviewed, written with the same cursor, compared with the dry run, and only then advanced with `nextCursor`:

```bash
while :; do
  Q="limit=100${CURSOR:+&afterId=$CURSOR}"; P="$E-${CURSOR:-0}"
  # 1. Dry run (dryRun=true): writes nothing.
  curl -sS --fail-with-body -X POST "$GW/api/admin/$E/public-addresses/backfill?$Q&dryRun=true" \
    -H "Authorization: Bearer $TOKEN" > "dry-$P.json" || break
  # 2. Review: collision suffixes and place-/user- fallbacks.
  jq '.items[] | select(.hadCollision or (.slug | test("^(place|user)(-|$)")))' "dry-$P.json"
  # 3. Unwanted slug? Stop here and initialize that owner by hand BEFORE writing this page:
  #    POST $GW/api/admin/$E/public-addresses/{id}/initialize  {"slug":"…","expectedRevision":0,"reason":"…"}
  #    then run the loop again; it resumes at this page with a new dry run.
  read -rp "Write page $P? [y/N] " OK; [ "$OK" = y ] || break
  # 4. Write the same page (dryRun=false, same cursor).
  curl -sS --fail-with-body -X POST "$GW/api/admin/$E/public-addresses/backfill?$Q&dryRun=false" \
    -H "Authorization: Bearer $TOKEN" > "write-$P.json" || break
  # 5. The written {entityId, slug} pairs must equal the dry run.
  diff <(jq -S '[.items[] | {entityId, slug}]' "dry-$P.json") \
       <(jq -S '[.items[] | {entityId, slug}]' "write-$P.json") || break
  # 6. Next page.
  CURSOR=$(jq -r '.nextCursor // empty' "write-$P.json"); [ -z "$CURSOR" ] && break
done
```

The loop stops on any HTTP error, on a declined prompt and on a diff. `CURSOR` still points at the page that stopped, so running the loop again resumes there, dry run first.

Review checklist:

- Duplicate city names. The second city in GUID order gets a suffix.
- RU/BY spelling variants, which produce different slugs: `grodno`/`grodna`, `mogilyov`/`magilyou`.
- `place-…` fallbacks: shop, roaster, city or zone names that yield no usable slug.
- Same-name zones in two cities. Zone slugs are global, so the second zone gets a suffix.
- For users: transliteration collisions such as `Ivan`/`ivan`/`Иван`, which all produce `ivan`.

**Why page by page.** The proposals set is per request, and dry-run writes nothing. A pure dry-run sweep therefore reports a duplicate on a later page as `hadCollision: false`, while write mode would suffix it. Always write page N before dry-running page N+1. `limit=100` keeps page boundaries rare. The live rehearsal tests (`ShopsBackfillRehearsalTests`, `AccountBackfillRehearsalTests`) pin this behavior.

**Replay check.** After the last page, resubmit the first page with `dryRun=false`. Every item must report `alreadyInitialized: true`:

```bash
curl -sS --fail-with-body -X POST "$GW/api/admin/$E/public-addresses/backfill?limit=100&dryRun=false" \
  -H "Authorization: Bearer $TOKEN" | jq -e 'all(.items[]; .alreadyInitialized)'
```

**Conflict.** A 409 "Address is reserved or changed concurrently." means another writer took a slug while the page was being written. The page was rolled back. Re-run the same page, dry run first. This is D-05 for Shops; Account already behaved this way.

**AccountService.** Only after step 5 is decided and AccountService's flag is on (step 6), run the same loop against `$GW/api/admin/users/public-addresses/backfill` instead of `$GW/api/admin/$E/public-addresses/backfill`. The gateway sends it through `account-admin-route` (Admin policy). There is no manual initialize for users, so step 3 of the loop does not apply: a slug changes only through a username change.

## Catalog filter values

Search and map query contracts are unchanged and GUID-based. No slug query parameters exist. The external frontend composes readable page and filter URLs, then resolves them to GUIDs before calling `GET api/CoffeeShops`.

| Filter | Query param | Readable value source |
| --- | --- | --- |
| City | `cityId` | `GET api/Catalogs/cities/by-slug/{slug}` (GUID in `data.id`); reverse mapping via `GET api/Catalogs/cities/public-addresses?ids=` |
| Roaster | `roasters` | `GET api/roasters/by-slug/{slug}`; reverse mapping via `GET api/roasters/public-addresses?ids=` |
| Tag | `tags` | `GET api/Catalogs/shop-tags` returns `{id, slug}` for each active tag |
| Coffee zone | none | No search filter exists; zone pages use the `api/Catalogs/coffee-zones` endpoints |
| Brew method, bean, equipment | `brewMethods`, `beans`, `equipments` | GUID only; no readable value in this phase |

The tag slug is immutable and unique, but it uses underscores (for example `laptop_friendly`). It is not a public address and is not validated by public-address rules, so use it as-is. Enum and other params (`type`, `priceRange`, `isOpen`, `isNew`, `isVisited`, `minRating`, `q`, `page`, `pageSize`) keep their existing values.

## Frontend handoff and rollback

The external frontend should call by-slug, render `data`, emit a canonical tag for `address.canonicalPath`, and issue a real HTTP 301 from an alias page or legacy GUID page to that fixed relative path. It must not construct redirect targets from the requested slug. This applies to city, coffee zone and `/users/{slug}` pages as well.

Operational rollback is to set `PublicAddresses__Enabled=false` and keep serving the existing GUID routes. Retain the registry and audit tables so published aliases and tombstone reservations are not lost. Do not run the migration `Down` after any addresses have been allocated. In particular, the `AddCityAndZonePublicAddresses` `Down` restores `"Kind" IN (1, 2)` and drops the city and zone registries, so it must not run after any city or zone address was allocated. Likewise, the Account `AddUserPublicAddresses` `Down` drops the user registry, so it must not run after any user address was allocated. For AccountService, rollback means setting its own `PublicAddresses__Enabled=false`: deletion keeps retiring addresses, and `GET api/Users/{id}` keeps hiding deleted and blocked users.

### Rollback rehearsal

Rehearse the rollback in a non-production environment before relying on it, once per service:

1. Set that service's `PublicAddresses__Enabled=false` and restart it.
2. The by-slug, batch (`public-addresses?ids=`), legacy-GUID (`{id}/public-address`) and admin address endpoints return 503.
3. Existing GUID routes are unchanged, for example `GET api/CoffeeShops/{id}` and `GET api/Users/{id}`.
4. Deletions keep retiring addresses: delete a test entity, and its slugs stay reserved as tombstones.
5. Set the flag back to `true` and restart. Slugs allocated earlier resolve again.
6. Never run a migration `Down`. It drops populated registries, and with them every alias and tombstone reservation; the Account `Down` succeeds and destroys the user registry.

The data-level part (503 while disabled, retirement while disabled, resolution after re-enabling) is automated as G-S11 and G-A9 and passed in the recorded [Live gate evidence](#live-gate-evidence).
