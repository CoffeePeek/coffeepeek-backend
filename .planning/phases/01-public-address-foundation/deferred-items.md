# Deferred items

- Live PostgreSQL verification: Docker daemon not running, no psql tool; approved `sudo -n dockerd` requires a password. Before enabling writers/backfill, run isolated PostgreSQL cases for competing slug allocation, canonical uniqueness, alias restoration, FK restriction/retirement and transactional rollback.
- Existing dependency audit warnings from builds: Microsoft.OpenApi 2.0.0 NU1903 (GHSA-v5pm-xwqc-g5wc), OpenTelemetry.Api 1.15.0 NU1902 (GHSA-g94r-2vxg-569j). Not introduced or changed here.
- Existing nullable warnings in Shared.Kernel/Shared.Web and xUnit analyzer warnings in unrelated tests. Left unchanged.
