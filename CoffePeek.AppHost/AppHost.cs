using CoffeePeek.Shared.Kernel;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17.7")
    .WithDataBindMount("./postgres-data")
    .WithEndpoint("tcp", e => e.Port = 5432);

var accountDb = postgres.AddDatabase(AppResources.AccountDb);
var shopsDb = postgres.AddDatabase(AppResources.ShopsDb);
var moderationDb = postgres.AddDatabase(AppResources.ModerationDb);
var mediaDb = postgres.AddDatabase(AppResources.MediaDb);
var redis = builder.AddRedis("redis")
    .WithHostPort(6379);

var shopsService = builder
    .AddProject<Projects.CoffeePeek_ShopsService>(AppResources.ShopsService)
    .WithReference(shopsDb)
    .WithEnvironment("DOTNET_ASPIRE", "true");

var moderationService = builder
    .AddProject<Projects.CoffeePeek_ModerationService>(AppResources.ModerationService)
    .WithReference(moderationDb)
    .WithEnvironment("DOTNET_ASPIRE", "true");

var accountService = builder
    .AddProject<Projects.CoffeePeek_AccountService>(AppResources.AccountService)
    .WithReference(accountDb)
    .WithReference(redis)
    .WithReference(shopsService)
    .WithReference(moderationService)
    .WithEnvironment(
        "AccountDeletionOptions__TokenPepper",
        builder.Configuration["AccountDeletionOptions:TokenPepper"]
        ?? "development-only-account-deletion-pepper-change-me")
    .WithEnvironment("RedisOptions__Host", "localhost")
    .WithEnvironment("RedisOptions__Port", "6379")
    .WithEnvironment("DOTNET_ASPIRE", "true");

var mediaService = builder
    .AddProject<Projects.CoffeePeek_MediaService>(AppResources.MediaService)
    .WithReference(mediaDb)
    .WithReference(shopsService)
    .WithEnvironment("DOTNET_ASPIRE", "true");

// Check-in photo resolve and user existence lookups call these over HTTP (not the gateway).
shopsService.WithReference(accountService).WithReference(mediaService);
moderationService.WithReference(shopsService).WithReference(mediaService);

builder.AddProject<Projects.CoffeePeek_Gateway>(AppResources.Gateway)
    .WithReference(redis)
    .WithReference(accountService)
    .WithReference(shopsService)
    .WithReference(moderationService)
    .WithReference(mediaService)
    .WithEnvironment("DOTNET_ASPIRE", "true")
    .WithEnvironment("DOTNET_ASPIRE_RUNNING", "true")
    .WithEnvironment("RedisOptions__Host", "localhost")
    .WithEnvironment("RedisOptions__Port", "6379")
    .WithUrl("/scalar", "docs");

builder.Build().Run();
