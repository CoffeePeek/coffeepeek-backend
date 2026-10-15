# CoffeePeek agent guide

CoffeePeek is a backend-only .NET 10 microservices platform. Five ASP.NET Core services run behind a YARP gateway; there is no frontend in this repository. Work with the application over HTTP through the gateway.

## Architecture and repository layout

| Service | Responsibility | Projects |
| --- | --- | --- |
| Gateway | YARP routes, JWT validation, CORS, rate limiting, Scalar | `CoffeePeek.Gateway` |
| Account | Users, authentication, profiles, statistics | `CoffeePeek.Account.*`, `CoffeePeek.AccountService` |
| Shops | Published shops, discovery, coffee catalog, check-ins, owners | `CoffeePeek.Shops.*`, `CoffeePeek.ShopsService` |
| Moderation | Suggestions, reports, imports, review queue | `CoffeePeek.Moderation.*`, `CoffeePeek.ModerationService` |
| Media | Presigned uploads and photo metadata | `CoffeePeek.MediaService` |

The four data services own separate PostgreSQL databases. They use layered Domain, Application, Infrastructure and Persistence code (Media keeps these concerns in one project). Wolverine handles CQRS and RabbitMQ messages with a PostgreSQL outbox. Redis supports caching and account/session concerns; MinIO stores photos. `CoffeePeek.Contract` contains shared DTOs and events. Shared projects provide domain, persistence, authentication, web and kernel code. `CoffePeek.AppHost` and `CoffePeek.ServiceDefaults` provide Aspire development orchestration and defaults.

These existing names have spelling inconsistencies and must be preserved unless a deliberate rename updates all references: `CoffePeek.*`, `CoffeePeek.Shops.Persistance`, and `CoffeeShop.Moderation.Persistence`.

Within Shops, `Shops`, `Menus`, `Community`, `CoffeeCatalog`, `Discovery` and `AppDistribution` have separate business ownership even though they share `ShopsDbContext` as a transaction and migration boundary. Put writes in the owning module and use narrow application ports for cross-module effects; Discovery may join other modules' tables for read projections but must not write them. `PublicAddresses` is a technical component for slug registry/history. See `docs/shops-module-ownership.md` for the current ownership map.

Admin and owner shop-profile updates share a profile revision. Existing profile PUTs accept optional `If-Match` and return 409 on a stale revision; new profile writer paths must advance `ProfileRevision` and preserve EF concurrency handling. Community owns check-ins, votes, reports and `CheckInPhotos`; the shop-profile gallery uses separate `ShopPhotos`. Preserve Media photo IDs and protected URLs when changing this boundary. See `docs/shops-architecture-decisions.md` and `docs/shops-community-photo-transition.md` before changing profile or photo persistence.

## C# layout and service conventions

- Put **one top-level type per C# file**, including classes, records, structs, interfaces, enums, DTOs, events and commands. Name the file after its type. Partial implementations may use `TypeName.Purpose.cs`. When modifying a bundled file, split its types while preserving namespaces and wire contracts. Apply the same rule to repository-owned generators.
- Application handlers generally live under `Application/Features/<Feature>/<UseCase>/`. Wolverine discovers their `Handle` methods through service registration. Service hosts keep `Program.cs` thin; registration and middleware usually live in `InfrastructureExtensions.cs` or `DependencyInjection.cs`.
- Gateway routes and clusters live under `ReverseProxy` in `CoffeePeek.Gateway/appsettings.json`. The gateway validates JWTs, strips client-supplied `X-User-*` headers and forwards signed identity headers. `GatewayAuth__SecretKey` must match in the gateway and services.
- Production uses pre-generated Wolverine handlers under each service's `Internal/Generated/WolverineHandlers/`. After changing a handler signature or adding a handler, run `dotnet run -- codegen write` from the affected service host with its required configuration, and commit the generated code.
- Use Context7 when current library or API documentation is needed.

## Build, tests and dependency audit

From the repository root:

```text
dotnet restore CoffeePeek.Backend.ci.slnf
dotnet tool restore
dotnet build CoffeePeek.Backend.ci.slnf -c Release
dotnet test CoffeePeek.Backend.ci.slnf -c Debug
dotnet list CoffeePeek.Backend.ci.slnf package --vulnerable --include-transitive
```

`dotnet tool restore` installs the repository-pinned `dotnet-ef` from `.config/dotnet-tools.json` for migration commands. `CoffeePeek.slnx` includes additional projects such as Aspire; use it when checking the full solution. CI uses `CoffeePeek.Backend.ci.slnf`, runs .NET tests, Python coffee-collector tests, the Node Cloudflare relay test, and live Shops integration and fault tests with PostgreSQL, RabbitMQ, Redis and MinIO. Some live tests need the environment variables shown in their test or CI setup. There is no separate linter; build analyzers and the dependency audit are the checks.

## Local development

Run `python dev/security/scan_secrets.py install --hooks` once per checkout and
`python dev/security/scan_secrets.py scan` before pushing. `.gitleaksignore` contains only
reviewed historical finding fingerprints; do not regenerate it to suppress new findings.
The 2026-10-15 exposure review is recorded in `docs/security/secrets.md`: the owner
confirmed Google OAuth, Yandex and Resend keys replaced; remaining credentials belong
to retired infrastructure. Do not reopen those findings without evidence of reuse.

The supported end-to-end helper path uses Bash, Docker and the scripts in `dev/` (run from the repository root):

```text
docker compose -f dev/docker-compose.dev.yml up -d
dev/migrate-and-seed.sh
dev/run-services.sh
dev/hello-world.sh
dev/stop-services.sh
```

Compose starts PostgreSQL (four service databases), RabbitMQ, Redis and MinIO, including local buckets. `dev/dev.env` contains local-only credentials. `dev/run-services.sh --no-build` skips its build. The script launches built DLLs on gateway `8080`, account `5353`, shops `5243`, moderation `6453`, and media `5025`, with logs under `dev/logs/`. `dev/hello-world.sh` exercises registration, email confirmation, login and basic gateway requests.

Aspire is an alternative development host in `CoffePeek.AppHost`. Its current AppHost creates PostgreSQL and Redis resources and starts the five projects; RabbitMQ and MinIO must be started separately and the services supplied with their required configuration. The `dev/` path above is the complete local setup.

Configuration details that matter when starting services or applying migrations:

- In Development, EF reads `ConnectionStrings__<dbname>` (names in `CoffeePeek.Shared.Kernel/AppResources.cs`), while Wolverine's outbox and design-time EF factories use `PostgresCpOptions__ConnectionString`. Both must target the same service database. The helper scripts set them.
- `MediaPublicUrlOptions__PublicEndpoint` must be a valid HTTP(S) URL. Media also requires `Sentry__Dsn`; an empty value disables Sentry.
- Account issues JWTs and Gateway validates them, so their `JWTOptions__SecretKey` values must match and be at least 32 characters. The shared `GatewayAuth__SecretKey` must also match across gateway and services.
- Registration needs a `User` role row, which migrations alone do not seed. `dev/migrate-and-seed.sh` adds it. Local login needs confirmed email; the smoke script performs confirmation using the stored token because Resend is not configured locally.
- Media exposes `/health` but not `/alive`. The other service hosts and Gateway use service defaults for both endpoints.

## Database migrations

EF migration history is required for fresh installs and upgrades; do not remove historical migrations or model snapshots merely because their tables were later retired. The Makefile provides `make mig-acc n=Name`, `mig-shops`, `mig-mod`, `mig-media` to add migrations and `make up-acc`, `up-shops`, `up-mod`, `up-media` to apply them. For example:

```text
dotnet ef migrations add MigrationName --project CoffeePeek.Account.Persistence --startup-project CoffeePeek.AccountService --context CoffeePeek.Account.Persistence.Configuration.AccountDbContext
dotnet ef database update --project CoffeePeek.Account.Persistence --startup-project CoffeePeek.AccountService
```

Apply migrations before starting services: local helpers run `dotnet ef database update`, and deployment scripts have their own migration step. Service startup does not call the migration helper. Write migrations so they are safe against existing production data. PostgreSQL's `xmin` is a system column: if EF scaffolds `AddColumn("xmin")` for row-version concurrency, empty that migration body rather than adding the column.

## Deployment

Production deployment uses `deploy/docker-compose.yml`, Caddy and `deploy/scripts/`. The GitHub workflow is `.github/workflows/ci-cd.yml`. See `README.md` for operator entry points and current API route groups.
