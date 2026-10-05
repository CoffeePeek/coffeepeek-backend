# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Architecture Overview

CoffeePeek is a **backend-only** .NET 10 microservices platform (no in-repo client) with these services:

- **API Gateway** (YARP): Routes requests, enforces JWT authentication, handles CORS, rate limiting, response caching
- **Account Service**: User accounts, email + Google OAuth, JWT token generation
- **Shops Service**: Coffee shop discovery, search, management
- **Moderation Service**: Content moderation (shop suggestions, reviews)
- **Media Service**: File uploads and media assets (MinIO/S3)

Each service follows **layered DDD**:
- **Domain** → **Application** (CQRS with Wolverine) → **Infrastructure** → **Persistence** (EF Core)

## Project Structure

**Backend** (layered per bounded context):
- `CoffeePeek.Account.*` (Domain, Application, Infrastructure, Persistence) → `CoffeePeek.AccountService`
- `CoffeePeek.Shops.*` (Domain, Application, Infrastructure, Persistance) → `CoffeePeek.ShopsService`
- `CoffeePeek.Moderation.*` (Domain, Application, Infrastructure) → `CoffeePeek.ModerationService` + `CoffeeShop.Moderation.Persistence`
- `CoffeePeek.MediaService` (single service, all layers inside)
- `CoffeePeek.Gateway` (YARP reverse proxy + Scalar OpenAPI docs)

**Shared Libraries**:
- `CoffeePeek.Shared.Kernel` — FluentResults, base abstractions
- `CoffeePeek.Shared.Domain` — Domain primitives
- `CoffeePeek.Shared.Persistence` — EF Core, Wolverine, PostgreSQL, RabbitMQ, Redis integration
- `CoffeePeek.Shared.Auth` — JWT bearer auth, user context from headers
- `CoffeePeek.Shared.Web` — OpenAPI, API versioning, CORS, exception handling, Serilog/Sentry
- `CoffeePeek.Contract` — Shared DTOs and event contracts

**Development Infrastructure**:
- `CoffePeek.AppHost` — Aspire orchestration (note: typo in folder name)
- `CoffePeek.ServiceDefaults` — Health checks, OpenTelemetry, service discovery defaults

## Key Technologies

| Concern | Technology |
|---|---|
| Runtime | .NET 10, C# 14 |
| CQRS / Messaging | Wolverine 5.30 + RabbitMQ |
| ORM | EF Core 10 + Npgsql (PostgreSQL 17) |
| Reverse Proxy | YARP 2.3 |
| Caching / Sessions | Redis (StackExchange.Redis) |
| Dev Orchestration | Aspire 13.2 |
| Logging / Tracing | Serilog 10, OpenTelemetry, Sentry |
| Object Storage | MinIO SDK |
| Email | Resend |
| OAuth | Google Auth SDK |
| Testing | xUnit v3, FluentAssertions, Moq, coverlet |

Central package management via `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`).

## Building and Running

### Prerequisites
- .NET 10 SDK
- Docker Desktop (for Aspire + PostgreSQL container)

### Build
```
dotnet restore CoffeePeek.slnx
dotnet build CoffeePeek.slnx
```

### Full Stack (Aspire)
```
cd CoffePeek.AppHost
dotnet run
```
Starts all services + PostgreSQL containers. Gateway at `http://localhost:5000`, API docs at `http://localhost:5000/scalar`.

### Individual Services
```
cd CoffeePeek.AccountService && dotnet run
cd CoffeePeek.ShopsService && dotnet run
cd CoffeePeek.ModerationService && dotnet run
cd CoffeePeek.MediaService && dotnet run
cd CoffeePeek.Gateway && dotnet run
```

### Tests
```
dotnet test CoffeePeek.slnx
dotnet test CoffeePeek.Account.Domain.Tests
dotnet test CoffeePeek.slnx --filter Name=TestMethodName
dotnet test CoffeePeek.slnx --collect:"XPlat Code Coverage"
```

## Database Migrations

Use the `Makefile` targets:
```
make mig-acc n=MigrationName     # add migration — Account
make mig-shops n=MigrationName   # add migration — Shops
make mig-mod n=MigrationName     # add migration — Moderation
make mig-media n=MigrationName   # add migration — Media

make up-acc     # apply — Account
make up-shops   # apply — Shops
make up-mod     # apply — Moderation
make up-media   # apply — Media
```

Manual equivalent:
```
dotnet ef migrations add MigrationName \
  --project CoffeePeek.Account.Persistence \
  --startup-project CoffeePeek.AccountService \
  --context CoffeePeek.Account.Persistence.Configuration.AccountDbContext

dotnet ef database update \
  --project CoffeePeek.Account.Persistence \
  --startup-project CoffeePeek.AccountService
```

## CQRS with Wolverine

Handlers live in `Application/Features/<Feature>/<UseCase>/`. The pattern:

```csharp
// Command or Query
public record CreateAccountCommand(string Email, string PasswordHash);

// Handler — static method, dependencies injected as parameters
public class CreateAccountHandler
{
    public static async Task<AccountCreatedResponse> Handle(
        CreateAccountCommand cmd,
        IAccountRepository repo,
        IUnitOfWork uow,
        CancellationToken ct)
    { ... }
}
```

Invoked via `IMessageBus`:
```csharp
var result = await messageBus.InvokeAsync<AccountCreatedResponse>(new CreateAccountCommand(...));
```

Handlers are discovered via assembly scanning in each service's `DependencyInjection.cs`.

## Service Host Pattern

Each service host has a thin `Program.cs`:
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddApplication();
var app = builder.Build();
app.UseApplication();
app.Run();
```

All registration (Serilog, Aspire defaults, Wolverine, EF Core, middleware, endpoints) is in `InfrastructureExtensions.cs` or `DependencyInjection.cs`.

## Persistence Pattern

```csharp
services.AddPersistence<AccountDbContext>(...)
    .AddRepository<IAccountRepository, AccountRepository>()
    .AddQueryService<IAccountQueryService, AccountQueryService>()
```

`CoffeePeek.Shared.Persistence` wires up EF Core + Npgsql, Wolverine PostgreSQL outbox, RabbitMQ auto-provisioning, and Redis.

## Gateway Routing (YARP)

Routes and clusters are defined in `appsettings.json` under `ReverseProxy` — no code changes needed to add a route. Clusters use service discovery names matching Aspire service names.

JWT validation happens at the edge. Downstream services extract claims via `ClaimsPrincipalExtensions` from `CoffeePeek.Shared.Auth`.

## Known Naming Inconsistencies

These typos exist in folder/project names and are accepted as-is:
- `CoffePeek.*` (AppHost, ServiceDefaults) — missing one `e`
- `CoffeePeek.Shops.Persistance` — missing `s` in Persistence
- `CoffeeShop.Moderation.Persistence` — wrong prefix (`CoffeeShop` vs `CoffeePeek`)

## CI/CD

GitHub Actions in `.github/workflows/ci-cd.yml`:
- Triggers: push to `dev` / `main`, pull requests
- Steps: restore workloads + NuGet (with caching), vulnerability audit, `dotnet build`, `dotnet test`


Always use Context7 when I need library/API documentation, code generation, setup or configuration steps without me having to explicitly ask.
