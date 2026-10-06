# AGENTS.md

General architecture, conventions, and build/test/migration commands live in `CLAUDE.md`
(read it first). This file only adds cloud-agent operating notes.

Follow the C# file-layout rule in `CLAUDE.md`: one top-level type per file, including DTOs, records, events, and commands.

## Cursor Cloud specific instructions

CoffeePeek is a backend-only .NET 10 microservices platform. Five ASP.NET Core services run
behind a YARP gateway and talk to Postgres (4 DBs), RabbitMQ (Wolverine bus), Redis, and MinIO.
There is no in-repo frontend; you interact with the system over HTTP through the gateway.

### What the update script / snapshot already provide
- .NET 10 SDK at `/usr/share/dotnet` (`dotnet` on `PATH`), `dotnet-ef` global tool, and Docker
  engine are pre-installed. The startup update script runs `dotnet restore CoffeePeek.Backend.ci.slnf`.
- Do NOT reinstall these in normal work.

### Running everything (dev mode)
Helper scripts live in `dev/` (all commands from repo root):
1. Start the Docker daemon (it does not persist across sessions; systemd is not managing it):
   `sudo dockerd > /tmp/dockerd.log 2>&1 &` then wait until `docker info` succeeds.
2. Start backing services: `docker compose -f dev/docker-compose.dev.yml up -d`
   (Postgres+4 DBs, RabbitMQ, Redis, MinIO+buckets; local-dev credentials in `dev/dev.env`).
3. Apply migrations + seed roles: `dev/migrate-and-seed.sh`  (idempotent).
4. Run the five services: `dev/run-services.sh`  (`--no-build` to skip the build).
   Ports: gateway `8080`, account `5353`, shops `5243`, moderation `6453`, media `5025`.
   Logs: `dev/logs/<service>.log`. Stop with `dev/stop-services.sh`.
5. Smoke test end to end: `dev/hello-world.sh` (register → confirm email → login → `/api/Users/me`
   → `/api/CoffeeShops`, all through the gateway).

### Non-obvious gotchas (important)
- Config is injected via env vars, not committed appsettings. In `Development` each data service
  reads its EF connection from `ConnectionStrings__<dbname>` (Aspire Npgsql, names in
  `CoffeePeek.Shared.Kernel/AppResources.cs`) AND the Wolverine outbox from
  `PostgresCpOptions__ConnectionString`; both must point at the same DB. `dev/dev.env` +
  `dev/run-services.sh` set all of this. The design-time EF factory only reads
  `PostgresCpOptions__ConnectionString`, so migrations need it too.
- `MediaService` fails to start unless `Sentry__Dsn` is set (empty string disables Sentry).
- `MediaPublicUrlOptions__PublicEndpoint` is validated as a URL on startup for every service —
  it must be a valid `http(s)` URL or the host refuses to start.
- The Account service issues JWTs and the Gateway validates them, so `JWTOptions__SecretKey`
  (min 32 chars) must be identical for both (see `dev/dev.env`).
- Registration requires a seeded `Roles` row named `User`; roles are NOT auto-seeded by migrations
  (`dev/migrate-and-seed.sh` inserts them). Login also requires a confirmed email — grab
  `EmailConfirmationToken` from `cpaccountdb."Users"` and `PUT /api/Users/me/email-confirmation?token=…`
  (email delivery via Resend is not configured locally).
- `MediaService` has no `/health` endpoint, so the gateway's `media-cluster` active health check
  marks it unhealthy and gateway-proxied `/api/Photos` may be reported down even though the service
  runs; call media directly on `:5025` if needed.
- Known pre-existing app bug (not an env issue): `GET /openapi/v1.json` returns 500 (the pinned
  `Microsoft.OpenApi` 2.0.0), so the gateway Scalar UI at `/scalar/` loads but its endpoint list
  stays empty. Exercise the API with curl instead.
- `dev/run-services.sh` launches the built DLLs directly (not `dotnet run`) so PIDs can be stopped
  cleanly; `dotnet run` leaves an orphaned child holding the port on kill.

### Tests / lint
- Tests need no infra: `dotnet test CoffeePeek.Backend.ci.slnf -c Debug` (492 tests: Account & Shops
  domain/application/infrastructure).
- There is no separate linter; the CI gate is the analyzer warnings from `dotnet build` plus the
  vulnerability audit `dotnet list CoffeePeek.Backend.ci.slnf package --vulnerable --include-transitive`.
