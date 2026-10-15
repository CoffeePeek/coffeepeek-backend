# CoffeePeek Backend

CoffeePeek is a platform for discovering specialty coffee shops: search and map, check-ins, owner
tools, and a moderation pipeline for user suggestions and bulk imports (OSM, files).
This repository is the **backend only** — a set of .NET 10 microservices behind a YARP gateway.

## Contents
1. [Architecture](#architecture)
2. [Tech Stack](#tech-stack)
3. [Repository Layout](#repository-layout)
4. [Getting Started](#getting-started)
5. [Configuration and Secrets](#configuration-and-secrets)
6. [Messaging and Wolverine Code Generation](#messaging-and-wolverine-code-generation)
7. [Database Migrations](#database-migrations)
8. [Testing](#testing)
9. [API](#api)
10. [Deployment](#deployment)

## Architecture

| Service | Responsibility |
|---|---|
| **Gateway** (YARP) | Single entry point: JWT validation, CORS, per-client rate limiting, routing, Scalar API docs |
| **Account** | Users, email and OAuth sign-in, JWT issuing, profiles, account deletion, user statistics |
| **Shops** | Published catalog: shops, search, map/clusters, coffee zones, check-ins, menus (OpenAI vision parsing), owner and admin tools, app downloads |
| **Moderation** | Queue for shop/roaster suggestions, canonical check-in submissions, shop change requests and issue reports; import pipeline (OSM Overpass, files, Google Places status, Yandex geocoding); audit log |
| **Media** | Uploads and photo storage (MinIO / S3) |

Each bounded context is layered DDD: **Domain → Application** (CQRS handlers on Wolverine) **→ Infrastructure →
Persistence** (EF Core, one PostgreSQL database per service).

**Authentication.** The gateway validates the JWT and forwards the identity to services as `X-User-*` headers
signed with a shared secret (`X-Gateway-Auth`). Services trust identity only when that secret matches; client-sent
`X-User-*` headers are stripped at the gateway and never read directly.

**Cross-service flow.** Services communicate through RabbitMQ events with a durable PostgreSQL outbox/inbox.
Example: approving a shop in Moderation publishes `ModerationShopApprovedEvent`; Shops creates the catalog shop and
replies with `ModerationShopApproveCompleteResponse`; Account updates the submitter's statistics.

## Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10, C# 14 |
| CQRS / messaging | Wolverine 6 + RabbitMQ, PostgreSQL outbox/inbox |
| Data | EF Core 10 + Npgsql, PostgreSQL 17 |
| Caching | Redis (StackExchange.Redis) |
| Gateway | YARP, ASP.NET Core rate limiting, Scalar |
| Local orchestration | .NET Aspire 13 |
| Observability | Serilog, OpenTelemetry, Sentry |
| Storage | MinIO |
| External APIs | Google Sign-In, Resend (email), OpenAI (menu vision), Yandex Geocoder, Google Places, OSM Overpass |
| Tests | xUnit v3, FluentAssertions, Moq |

Package versions are managed centrally in `Directory.Packages.props`.

## Repository Layout

Projects are siblings in the repository root, grouped by bounded context:

```
CoffePeek.AppHost/                Aspire app host (local orchestration)
CoffePeek.ServiceDefaults/        Health checks, OpenTelemetry, service discovery
CoffeePeek.Gateway/               YARP gateway
CoffeePeek.AccountService/        Account host      + CoffeePeek.Account.{Domain,Application,Infrastructure,Persistence}
CoffeePeek.ShopsService/          Shops host        + CoffeePeek.Shops.{Domain,Application,Infrastructure,Persistance}
CoffeePeek.ModerationService/     Moderation host   + CoffeePeek.Moderation.{Domain,Application,Infrastructure}
                                                    + CoffeeShop.Moderation.Persistence
CoffeePeek.MediaService/          Media (single project)
CoffeePeek.Contract/              Shared DTOs and event contracts
CoffeePeek.Shared.*               Kernel, Domain, Persistence, Auth, Web
*.Tests/                          Test projects
deploy/                           Production Docker Compose, Caddy, deploy scripts
dev/                              Local infra compose and helper scripts
```

The folder-name typos `CoffePeek.*`, `Shops.Persistance` and `CoffeeShop.Moderation.Persistence` are intentional
(renaming would break references) — keep them.

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Docker Engine or Docker Desktop
- Rider, Visual Studio or VS Code with C# Dev Kit

```bash
git clone <repository-url>
cd CoffeePeek-NET
dotnet restore CoffeePeek.slnx
dotnet tool restore                 # repository-pinned dotnet-ef for migrations
dotnet build CoffeePeek.slnx
```

### Option 1 — Dev infrastructure and services on the host (recommended)

```bash
docker compose -f dev/docker-compose.dev.yml up -d   # Postgres, RabbitMQ, Redis, MinIO
dev/migrate-and-seed.sh                              # apply migrations, seed roles
dev/run-services.sh                                  # build and start all services (logs in dev/logs/)
dev/hello-world.sh                                   # end-to-end gateway smoke test
dev/stop-services.sh
```

The helper scripts use Bash and `dev/dev.env` for local-only connection settings. The gateway listens on
`http://localhost:8080` and API docs are at `http://localhost:8080/scalar`.

### Option 2 — Aspire app host

```bash
cd CoffePeek.AppHost
dotnet run
```

The current AppHost starts PostgreSQL and Redis resources and the five service projects. It does **not** create
RabbitMQ or MinIO resources. Start and configure those separately before using Aspire; supply each service's
required environment settings. The Aspire dashboard prints the actual gateway URL. For a standalone gateway
`dotnet run`, its HTTP launch profile uses `http://localhost:5169`.

### Option 3 — A single service

```bash
cd CoffeePeek.ShopsService && dotnet run
```

The service needs its database and required backing services reachable. Option 1 starts the local infrastructure;
its scripts also supply the connection strings and other required settings.

## Configuration and Secrets

- Configuration comes from `appsettings.json`, environment variables and .NET user secrets. Options are validated at
  startup, so a missing required value stops the service with a clear error.
- **`appsettings.Development.json` is git-ignored.** Keep real credentials there or in user secrets
  (`dotnet user-secrets`), never in tracked files. Docker builds exclude it via `.dockerignore`.
- **Gateway secret.** `GatewayAuth:SecretKey` must be identical in the gateway and every service. The key committed
  in `appsettings.json` is a public development value and is **rejected outside the Development environment**.
- **JWT secret.** Account issues tokens and Gateway validates them. Both must use the same `JWTOptions:SecretKey`.
- Optional integrations: `OpenAiOptions` (menu parsing), `YandexApiOptions` (geocoding), `GooglePlaces`
  (import status checks), `Sentry:Dsn`. See `deploy/.env.example` for the full production variable list.
- Install local secret checks once per checkout: `python dev/security/scan_secrets.py install --hooks`.
  They check staged changes before commit and history before push. CI runs the same scanner and blocks
  image publishing on new findings. See [secret handling and the completed exposure review](docs/security/secrets.md).

## Messaging and Wolverine Code Generation

Handlers are plain static `Handle` methods discovered by Wolverine:

```csharp
public static class UpdateModerationCoffeeShopStatusHandler
{
    public static async Task<(Response, object?)> Handle(
        UpdateModerationCoffeeShopStatusCommand command,
        IModerationShopRepository repository,
        CancellationToken ct) { ... }   // the second tuple item is published as an event
}
```

- Every handler runs inside an EF Core transaction; outgoing events go through the durable outbox and are sent only
  after commit.
- Only transient database conflicts (unique violations, serialization failures, optimistic-concurrency conflicts) are
  retried automatically; other failures go to the error queue.
- **Production loads pre-generated handler code** (`Internal/Generated/WolverineHandlers/` in each service host) and
  refuses to start if it is missing. After adding a handler or changing a handler's parameters or return type,
  regenerate and commit it:

  ```bash
  cd CoffeePeek.ShopsService      # or the affected service host
  dotnet run -- codegen write
  ```

  The command builds the host, so it needs the service's required configuration (development settings or
  environment variables).

## Database Migrations

Each service owns its database and migration history. Apply pending migrations before starting services;
`dev/migrate-and-seed.sh` handles local databases and deployment scripts handle production. Service startup
does not apply them. Preserve historical EF migrations and model snapshots for fresh installs and upgrades.

```bash
make mig-acc n=MigrationName     # add migration — Account
make mig-shops n=MigrationName   # add migration — Shops
make mig-mod n=MigrationName     # add migration — Moderation
make mig-media n=MigrationName   # add migration — Media

make up-acc                        # apply — Account
make up-shops                      # apply — Shops
make up-mod                        # apply — Moderation
make up-media                      # apply — Media
```

Manual equivalent:

```bash
dotnet ef migrations add MigrationName \
  --project CoffeePeek.Account.Persistence \
  --startup-project CoffeePeek.AccountService \
  --context CoffeePeek.Account.Persistence.Configuration.AccountDbContext
```

Row-version concurrency uses PostgreSQL's `xmin` system column: when EF scaffolds an `AddColumn("xmin")`,
empty the migration body — the column already exists.

## Testing

```bash
dotnet test CoffeePeek.slnx                                   # everything
dotnet test CoffeePeek.Moderation.Domain.Tests                # one project
dotnet test CoffeePeek.slnx --filter Name=TestMethodName      # one test
dotnet test CoffeePeek.slnx --collect:"XPlat Code Coverage"   # coverage
```

| Project | Covers |
|---|---|
| `*.Domain.Tests` | Aggregates, value objects, import parsers/classifiers, matchers |
| `*.Application.Tests` | Command/query handlers and application services (mocked dependencies) |
| `*.Infrastructure.Tests` | Message consumers, external clients, EF queries (InMemory provider) |
| `CoffeePeek.Gateway.Tests` | Route configuration, auth policies, rate limiting |
| `*Service.Tests` | Host wiring: OpenAPI generation, Wolverine pre-generated handlers, contracts |

CI runs the .NET test projects listed in `CoffeePeek.Backend.ci.slnf` — add new test projects there.
It also tests the Node Cloudflare relay and Python coffee collectors/deployment validation. Separate live Shops
integration and fault gates use PostgreSQL, RabbitMQ, Redis and MinIO.

## API

With the dev scripts, interactive documentation for all services is at `http://localhost:8080/scalar`.

Main route groups through the gateway:

| Routes | Service |
|---|---|
| `/api/Users/*`, `/api/Tokens/*` | Account |
| `/api/CoffeeShops/*`, `/api/Map/*`, `/api/Catalogs/*`, `/api/v1/check-ins/*`, `/api/menu/*`, `/api/roasters/*`, `/api/owner/*`, `/api/public/stats` | Shops |
| `/api/ModerationShops/*`, `/api/v1/moderation/check-ins/*`, `/api/ModerationRoasters/*`, `/api/ShopChangeRequests/*`, `/api/ShopIssueReports/*` | Moderation |
| `/api/Photos/*` | Media |
| `/api/admin/*` | Admin tools (Moderator/Admin roles) |
| `/download/*`, `/api/v1/app-downloads`, `/api/v1/app-version` | App download links, update policy |

Every service exposes `/health`. Gateway, Account, Shops and Moderation also expose `/alive`; Media does not.

## Deployment

Production runs on a VPS with Docker Compose (`deploy/docker-compose.yml`) behind Caddy (HTTPS). CI
(`.github/workflows/ci-cd.yml`) builds and runs the .NET, Node, Python and live Shops gates for pushes and
pull requests targeting `dev`/`main`. It publishes images on `main` and deploys to the VPS only on a `main` push.

```bash
cp deploy/.env.example deploy/.env    # fill in values
deploy/scripts/deploy.sh              # first deployment
deploy/scripts/update.sh              # subsequent updates
```

`deploy/scripts/ensure-secrets.sh` (run by both scripts) generates missing secrets such as `GATEWAY_AUTH_SECRET` and
`ACCOUNT_DELETION_TOKEN_PEPPER` without overwriting existing values. Containers run as a non-root user.

Client IPs follow Caddy → Gateway → AccountService. The dedicated `proxy` network gives Caddy and
Gateway fixed addresses; network-specific aliases keep both hops on that network. Each .NET host
accepts one `X-Forwarded-For` hop only from its configured `ForwardedHeaders__KnownProxies__0`.
Gateway trusts Caddy, AccountService trusts Gateway, and YARP replaces the outgoing header with the
verified client IP. Caddy remains the public edge and discards client-supplied forwarding headers.
User-Agent passes through unchanged. With no configured proxies, forwarding is disabled.

Deploy the Gateway/Account images, Compose file and Caddyfile together using `update.sh`; no database
migration is needed. `PROXY_NETWORK_PREFIX` defaults to `172.30.42` (a private /24); change it before
deployment if it overlaps an existing network. Other hosting setups must configure the exact proxy
addresses explicitly, including loopback proxies for local development. Do not enable blanket trust
with `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. A CDN before Caddy needs its own explicit trust configuration.
Existing session IPs cannot be recovered; a new login or refresh records the corrected IP in the new
session, which is returned by `GET /api/admin/users/{id}/sessions`.
