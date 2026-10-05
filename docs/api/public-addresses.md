# Public slugs

Every entity stores its current public address in its own table. `Shops`, `Roasters`, `Cities`, `CoffeeZones`, `CoffeeBeans`, `Equipments`, `BrewMethods` and `Users` have `Slug` and `SlugRevision` columns. `ShopTags` has `PublicSlug` and `SlugRevision`, alongside its existing internal tag key `Slug` (for example `laptop_friendly`). The public value is `laptop-friendly`.

Slugs have a unique index within their entity type. `SlugRevision` protects concurrent renames. There are no address registry or address audit tables in the current model.

## Address changes

- Only the current slug resolves. After a rename, the previous address returns 404 and is available for reuse.
- Physical deletion removes the slug with its owner. Soft-deleted users have their slug cleared. Hidden shops, unpublished zones, inactive tags and blocked/deleted users do not resolve publicly.
- Editing a shop/catalog display name leaves its slug unchanged. Admins can rename it explicitly. A profile slug follows username changes; the existing seven-day username cooldown remains.
- Reads accept uppercase and normalize it. Writes require a valid lowercase slug. The existing metadata shape is preserved: `revision` is the owner's `SlugRevision`, and `isAlias` is always false.
- Generation uses the name, with the street address for shops. Names are transliterated; collisions receive deterministic hash suffixes. Internal IDs are unchanged.

See [the client contract](public-addresses-client.md) for routes and response shapes.

## Migration and rollout

Apply these migrations together with the matching Account, Shops and Moderation binaries:

- Shops: `20261005080553_MoveSlugsToOwnerTables`.
- Account: `20261005080557_MoveUserSlugToUserTable`.
- Moderation: `20261005081410_RemoveLegacyImportSource`.

The migrations transfer existing current addresses before dropping the registries. Uninitialized owners receive generated slugs. Synthetic shop addresses consisting of a source prefix and an opaque hexadecimal identifier are regenerated from the actual shop name and address. Old aliases and deleted-owner reservations are discarded.

Retired import source value 3 becomes File (2), retaining candidates and published shops. Conflicting external IDs receive a unique `legacy-<candidate-id>` value. Verification signals use the generic `import:` namespace. Osm (1) and File (2) keep their numeric values.

Stop the three affected services while applying migrations: previous binaries still query the old tables. Restart them with the new binaries. Keep Account and Shops `PublicAddresses__Enabled=true` for the existing public slug APIs. The gateway, internal aggregate IDs and messaging contracts continue to use the existing routes/IDs.

Downgrading these migrations is deliberately refused because address history and retired source provenance are discarded. Restore the pre-rollout database backups and previous service binaries if rollback is required.

## Administrative operations

Admin routes retain GUID owner IDs. Shops kinds are `shops`, `roasters`, `cities`, `coffee-zones`, `beans`, `equipments`, `brew-methods`, `shop-tags`.

| Method | Path |
| --- | --- |
| POST | `/api/admin/{kind}/public-addresses/{id}/initialize` |
| PATCH | `/api/admin/{kind}/public-addresses/{id}` |
| POST | `/api/admin/{kind}/public-addresses/backfill?afterId={cursor}&limit=100&dryRun=true` |
| POST | `/api/admin/users/public-addresses/backfill?afterId={cursor}&limit=100&dryRun=true` |

Rename accepts `{"slug":"surf-coffee-center","expectedRevision":2,"reason":"Use the public brand name"}`. A stale revision or an occupied current slug returns 409. No history endpoint exists. User addresses follow usernames rather than manual admin rename.

Backfill remains useful for owners created while allocation was disabled: limits are 1–100, dry-run writes nothing, initialized rows are unchanged on replay, and `nextCursor` advances the owner page. Review each dry-run page before applying it.

## Verification

On 2026-10-05 the migration scripts were rehearsed on temporary copies of the server databases: 143 shops and 50 users retained their rows and received primary-table slugs; all address registry/audit tables were removed; 399 import candidates were retained and no retired source values remained. All 37 synthetic shop addresses became unique readable slugs. The temporary databases were removed after the rehearsal.

The same migrations were then applied to the server databases with the three affected services stopped. Account, Shops and Moderation were restarted with matching images built from this working tree. All owner row counts remained unchanged. Gateway checks confirmed all 37 renamed shop URLs return 200, all 37 previous URLs return 404, and a current user profile resolves successfully. All three services are healthy.

The server release is in `/opt/coffeepeek/releases/20261005-owner-slugs`. It contains private pre-migration dumps of `cpshopsdb`, `cpaccountdb` and `cpmoderationdb`, the applied SQL, reviewed slug mapping, deployment log and `result.json`. Running images are `coffeepeek-local/{account,shops,moderation}:20261005-owner-slugs`; their Compose override is `compose.yml` in the release directory. Include this change in the next normal image build before redeploying: older service images are incompatible with the migrated schema.

Run ordinary checks with `dotnet build CoffeePeek.Backend.ci.slnf -c Release` and the CI test projects. Current-slug tests cover old-address 404, collisions, reuse after deletion, visibility, backfill and model indexes. Real HTTP/YARP smoke verifies profile renames and public API response shapes. Optional PostgreSQL tests use `COFFEEPEEK_LIVE_PG` plus a password-free maintenance connection and `PGPASSFILE`; the harness creates and removes only its own `cp_live_*` databases.
