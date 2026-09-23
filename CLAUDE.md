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

<!-- GSD:project-start source:PROJECT.md -->
## Project

**CoffeePeek — Tech Debt Resolution**

CoffeePeek is a .NET 10 microservices platform for finding and discovering coffee shops. This project covers full resolution of the technical debt documented in `.planning/codebase/CONCERNS.md`: from critical security vulnerabilities and functional bugs to code refactoring, performance optimization, and restoring test coverage.

**Core Value:** Every bug, vulnerability, and performance issue found in CONCERNS.md must be fixed and covered by a test so that it can never come back.

### Constraints

- **Tech Stack**: .NET 10, C# 14 — do not change
- **Breaking changes**: Public API contracts (CoffeePeek.Contract) must not change without versioning
- **Naming typos**: Do not rename CoffePeek.*, Shops.Persistance, CoffeeShop.Moderation.* — accepted decision in CLAUDE.md
- **Migration safety**: Database schema changes (if needed) go through EF Core migrations via the Makefile
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Languages
- C# 14 — all backend services and shared libraries
- SQL — EF Core migrations (PostgreSQL dialect)
## Runtime
- .NET 10 (SDK 10.0.100, `rollForward: latestMajor`, allows pre-release)
- Pinned via `global.json`
- NuGet with Central Package Management (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`)
- All version numbers declared once in `Directory.Packages.props` — individual `.csproj` files reference packages without version attributes
## Frameworks
- ASP.NET Core 10 (`Microsoft.NET.Sdk.Web`) — all five services (Account, Shops, Moderation, Media, Gateway)
- YARP 2.3.0 — reverse proxy at `CoffeePeek.Gateway`
- Wolverine 5.30.0 — CQRS message bus and saga orchestration (WolverineFx, WolverineFx.EntityFrameworkCore, WolverineFx.Postgresql, WolverineFx.RabbitMQ)
- EF Core 10.0.5 — ORM across all services; `Z.EntityFramework.Extensions.EFCore` 10.105.4 for bulk operations
- `Asp.Versioning.Http` 8.1.1 + `Asp.Versioning.Mvc.ApiExplorer` 8.1.1 — API versioning on downstream services
- `Microsoft.AspNetCore.OpenApi` 10.0.5 — OpenAPI schema generation
- `Scalar.AspNetCore` 2.13.22 — OpenAPI UI served at the Gateway (`/scalar`)
- `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.5 — JWT validation at the Gateway (`CoffeePeek.Shared.Auth`)
- Rate limiting — built-in ASP.NET Core sliding window, configured in `CoffeePeek.Gateway/Extensions/RateLimitingExtensions.cs`
- xUnit v3 3.2.2 — test runner
- FluentAssertions 8.9.0 — assertion library
- Moq 4.20.72 — mocking framework
- coverlet.collector 8.0.1 — code coverage collection
- Bogus 35.6.5 — fake data generation (used in persistence test fixtures)
- .NET Aspire 13.2 — local dev orchestration (`CoffePeek.AppHost`)
- `CoffePeek.ServiceDefaults` — shared OpenTelemetry, service discovery, health checks for all services
## Key Dependencies
- `WolverineFx` 5.30.0 — CQRS bus; all handler dispatch, RabbitMQ publish/subscribe, PostgreSQL outbox pattern (`CoffeePeek.Shared.Persistence`)
- `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.1 — database driver; used in all four data DbContexts (Account, Shops, Moderation, Media)
- `StackExchange.Redis` 2.12.14 — Redis client; session storage and cache invalidation (`CoffeePeek.Shared.Persistence/Extensions/RedisConfiguration.cs`)
- `Minio` 7.0.0 — MinIO/S3 object storage client (`CoffeePeek.MediaService`)
- `Google.Apis.Auth` 1.73.0 — Google ID token validation (`CoffeePeek.Account.Application`)
- `Resend` 0.3.0 — transactional email SDK (`CoffeePeek.Account.Application`, `CoffeePeek.Account.Infrastructure`)
- `Sentry.AspNetCore` 6.3.1 — error tracking (`CoffeePeek.Shared.Web`)
- `Serilog.AspNetCore` 10.0.0 + `Serilog.Sinks.Console` 6.1.1 — structured logging (`CoffeePeek.Shared.Web`)
- `FluentResults` 4.0.0 — Result<T> error handling pattern (`CoffeePeek.Shared.Kernel`)
- `Mapster` 10.0.7 + `Mapster.DependencyInjection` 10.0.7 — object mapping (Account Application, Shops Application, Moderation Application)
- `Polly` 8.5.2 + `Microsoft.Extensions.Http.Polly` 10.0.0 — HTTP resilience
- `Microsoft.Extensions.Http.Resilience` 10.4.0 — Aspire standard resilience handler
- `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` 13.2.2 — Aspire integration for PostgreSQL + EF Core in Account, Shops, Media persistence projects
- `Aspire.Hosting.AppHost` 13.2.2 — Aspire AppHost SDK
- `Aspire.Hosting.PostgreSQL` 13.2.2, `Aspire.Hosting.RabbitMQ` 13.2.2, `Aspire.Hosting.Redis` 13.2.2 — container orchestration in `CoffePeek.AppHost`
- `Microsoft.Extensions.ServiceDiscovery` 10.4.0 + `Microsoft.Extensions.ServiceDiscovery.Yarp` 10.4.0 — Aspire service discovery
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3, `OpenTelemetry.Extensions.Hosting` 1.15.3, `OpenTelemetry.Instrumentation.AspNetCore` 1.15.2, `OpenTelemetry.Instrumentation.Http` 1.15.1, `OpenTelemetry.Instrumentation.Runtime` 1.15.1 — observability pipeline (`CoffePeek.ServiceDefaults`)
- `Microsoft.EntityFrameworkCore.Relational` 10.0.5 — relational mapping in Account and Shops persistence
- `System.IO.Hashing` 10.0.5 — used in Account Application
## Configuration
- Configuration sourced from `appsettings.json`, environment variables, and .NET User Secrets (`UserSecretsId` in AppHost)
- Options pattern throughout: all config sections are strongly typed with `AddValidateOptions<T>()` (DataAnnotations validation on startup)
- Key configuration sections (see `CoffeePeek.AccountService/appsettings.json`):
- `Directory.Packages.props` — central NuGet version manifest
- `global.json` — SDK version pin
- `Makefile` — EF Core migration helpers (`mig-acc`, `mig-shops`, `mig-mod`, `mig-media`, `up-*`)
- Per-service Dockerfiles: `AccountService.Dockerfile`, `ShopsService.Dockerfile`, `ModerationService.Dockerfile`, `MediaService.Dockerfile`, `Gateway.Dockerfile` (all use `mcr.microsoft.com/dotnet/aspnet:10.0` as runtime base)
## Platform Requirements
- .NET 10 SDK (10.0.100+)
- Docker Desktop (for Aspire-managed PostgreSQL 17, RabbitMQ, Redis containers)
- `dotnet workload restore CoffeePeek.slnx` required before first build (Aspire workload)
- Deployment target: VPS via Docker Compose (`deploy/docker-compose.yml`); Gateway clusters default to Aspire service discovery names
- Container images pushed to Docker Hub via GitHub Actions (`docker.io/<DOCKER_HUB_USERNAME>/coffeepeek.*`)
- Target OS: Linux (`DockerDefaultTargetOS=Linux` in all service `.csproj` files)
- PostgreSQL 17 (four separate databases: `account`, `shops`, `moderation`, `media`)
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Naming Patterns
- Classes: `PascalCase` matching class name, one class per file — `RegisterUserHandler.cs`, `UserRepository.cs`
- Test files: `{ClassName}Tests.cs` or `{ClassName}Test.cs` (both exist; `Tests` suffix is preferred)
- Commands/Queries: `{Action}{Entity}Command.cs` — `RegisterUserCommand.cs`, `UpdateProfileAboutCommand.cs`
- Handlers: `{Action}{Entity}Handler.cs` — `RegisterUserHandler.cs`, `LoginUserHandler.cs`
- Responses: `{Entity}Response.cs` or `{Action}EntityResponse.cs` — `LoginResponse.cs`, `CreateEntityResponse.cs`
- Interfaces: `I` prefix + PascalCase — `IUserRepository`, `IUnitOfWork`, `IJWTTokenService`
- Concrete classes: PascalCase, no prefix — `UserRepository`, `CachedUserRepository`
- Static handler classes use `public static class` — `RegisterUserHandler`, `LoginUserHandler`
- Non-static handler classes (when class-level constructor injection needed) use `public class` — `LoginUserHandler`, `AuthService`
- Value objects: `record` type with `Create(...)` factory method — `Email`, `Username`, `PhoneNumber`
- PascalCase — `RegisterAsync`, `GetById`, `SaveChangesAsync`
- Handler entry point is always named `Handle` — `public static async Task<T> Handle(...)`
- Factory methods on domain objects named `Create` or `Register` — `Email.Create(...)`, `User.Register(...)`
- camelCase for locals and parameters — `userId`, `passwordHash`, `cancellationToken`
- `_camelCase` for private readonly fields — `_userRepoMock`, `_unitOfWorkMock`, `_refreshTokens`
- Constants: `PascalCase` in static classes — `BusinessConstants.MaxActiveSessions`
- CancellationToken parameter conventionally named `ct` in handlers, `cancellationToken` in other methods
- Mirror directory structure — `CoffeePeek.Account.Application.Features.Auth.RegisterUser`
- Test namespace mirrors production namespace — `CoffeePeek.Account.Application.Tests.Features.Auth.Register`
## Code Style
- No `.editorconfig` or `.prettierrc` found — enforced via IDE (Rider) settings
- Standard C# formatting: 4-space indentation, opening braces on same line
- `using` file-level namespace declarations (`namespace X;` not `namespace X { }`)
- Trailing semicolons on single-line records
- `<Nullable>enable</Nullable>` in all production and test projects — nullable reference types enforced
- Nullable return types annotated with `?` — `Task<User?>`, `string?`
- Null-forgiving operator `!` used sparingly for post-condition guarantees — `user.Credentials.EmailConfirmationToken!`
- C# 14 / .NET 10 — use modern features: `record`, collection expressions `[]`, pattern matching
- Target-typed `new()` not used; prefer explicit `new ClassName(...)` or static factory
- `partial record` for source-generated types — `public partial record Email` using `[GeneratedRegex]`
- Primary constructors for services — `public class UserRepository(AccountDbContext dbContext)`
- Init-only properties on response/DTO types — `public bool IsSuccess { get; init; }`
## Import Organization
- `<ImplicitUsings>enable</ImplicitUsings>` — standard `System` namespaces auto-imported
- `global using` in test projects for frequently used type aliases — `global using DomainUser = CoffeePeek.Account.Domain.Entities.UserAggregate.User;`
- `<Using Include="Xunit"/>` in some test `.csproj` files to avoid repeating `using Xunit;` in every file
- None — all imports are fully qualified namespace paths
## Command / Record Design
- `[property: JsonIgnore]` on fields that come from the authenticated user context, not the request body — `UserId`, `DeviceName`, `IpAddress`
- `[property: MaxLength(N)]` directly on record properties for validation hints
- Controller merges context into command with `record with` expression: `command = request with { UserId = userContext.GetUserIdOrThrow() };`
## Wolverine Handler Pattern
- First parameter is always the command/query message
- Dependencies injected as subsequent parameters by Wolverine
- `CancellationToken ct` is the last parameter
- Handlers can return tuples `(TResponse, TEvent)` to publish side-effect events alongside returning a result
- Handlers are `public static class` when no instance state is needed; `public class` when constructor injection is required (rare)
- Handler invoked from controllers via `IMessageBus.InvokeAsync<TResult>(command)`
## Error Handling
- Registered with `services.AddExceptionHandler<GlobalExceptionHandler>()`
- Maps `BaseException.StatusCode` → HTTP status code
- In DEBUG: includes `StackTrace` and `InnerException`
- In Release: returns safe `"An unexpected error occurred."` for non-domain exceptions
- Returns `ErrorResponse` JSON body
## Response Pattern
- `Response` — non-generic success/error, has `IsSuccess`, `Message`, `Data?`
- `Response<TData>` — generic, typed `Data` property
- `CreateEntityResponse` — for POST create operations, adds `EntityId`
- `UpdateEntityResponse<T>` — for PATCH update operations
- Return typed response object directly — Wolverine serializes it
- Return tuple `(TResponse, TEvent)` to simultaneously return data and publish a domain event
## Logging
- Minimum level: `Information`
- Console sink with template: `[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}`
- Dev: `AnsiConsoleTheme.Code` (colored); Prod: no theme
- Read from `appsettings.json` via `ReadFrom.Configuration`
- Enriched with `FromLogContext()`
## Domain Object Design
- `Id` property
- `CreatedAtUtc` / `UpdatedAtUtc` audit timestamps
- `AddDomainEvent()` / `GetDomainEvents()` for domain events
- Private parameterless constructor for EF Core — `private User() { }`
## EF Core Configuration
- `CachedUserRepository` wraps `UserRepository` and invalidates Redis on writes
- Registered in DI manually: `services.AddScoped<IUserRepository, UserRepository>()` (the caching variant is registered separately when needed)
## Comments
- XML doc comments (`///`) on public controller actions, response types, and shared library public APIs
- Inline comments for non-obvious business logic — race conditions, security considerations, regression notes
- `// ReSharper disable once` suppressions where EF Core requires private constructors
## Module Registration
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## Pattern
```
```
## Layers (per bounded context)
```
```
## CQRS with Wolverine
```csharp
```
## Gateway Authentication Flow
- Global: 300 req/min
- Auth endpoints: 20 req/min
- Media endpoints: 10 req/min
## Cross-Service Messaging
## Caching
## Persistence Pattern
```csharp
```
## Aspire Orchestration
## Shared Libraries
| Library | Purpose |
|---------|---------|
| `CoffeePeek.Shared.Kernel` | FluentResults, base abstractions |
| `CoffeePeek.Shared.Domain` | Domain primitives |
| `CoffeePeek.Shared.Persistence` | EF Core, Wolverine, PostgreSQL, RabbitMQ, Redis setup |
| `CoffeePeek.Shared.Auth` | JWT bearer, `HeaderAuthenticationHandler`, `ClaimsPrincipalExtensions` |
| `CoffeePeek.Shared.Web` | OpenAPI, API versioning, CORS, exception handling, Serilog/Sentry |
| `CoffeePeek.Contract` | Shared DTOs and event contracts |
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->
## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
