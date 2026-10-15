# Canonical check-ins: database migration history

The current API uses `/api/v1/check-ins` and `/api/v1/moderation/check-ins`. The former review and check-in APIs are retired. The EF Core migrations that introduced canonical check-ins and removed the old tables remain in the repository because EF needs their history for fresh databases and upgrades.

| Database | Historical cleanup migration |
| --- | --- |
| Shops | `20261007160000_RemoveLegacyCheckInArchives` removes `CheckIns`, `Reviews`, `ReviewHelpfulVotes` and their obsolete photo links. |
| Moderation | `20261007160000_RemoveLegacyReviewSnapshots` removes `ModerationReviews` and its obsolete photo links. |
| Account | `20261007160000_RemoveLegacyReviewCounter` removes `Users.ReviewCount`. |
| Media | The canonical photo-claim migration is retained; no legacy table cleanup is needed. |

These cleanup migrations retain their data-protection checks. An existing database with nonempty legacy archives cannot run them through a direct EF update. Resolve that database's retained data separately before retrying; do not bypass the migration guards. The one-time source-data transfer utilities are no longer part of the application or deployment path.

## Applying migrations

`deploy/scripts/ci-deploy.sh` starts the backing services, stops the five application services, and runs `deploy/scripts/apply-migrations.sh` in the .NET SDK container. The manual `deploy/scripts/deploy.sh` uses the same container for its first deployment and later reruns. The migration script restores the repository's local `dotnet-ef` tool and runs `dotnet ef database update` for Account, Shops, Moderation and Media using their design-time factories. On failure, the application services remain stopped; after resolving the error, rerun deployment. On success, the deployment starts them again.

For local development, start `dev/docker-compose.dev.yml` and run `dev/migrate-and-seed.sh`. It applies the same four EF contexts with development connection strings and seeds the account roles. The local tool manifest pins the EF CLI version.
