using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Extentions;
using CoffeePeek.Shared.Persistence;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffeeShop.Moderation.Persistence.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.Moderation.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            builder.AddNpgsqlDbContext<ModerationDbContext>(
                connectionName: AppResources.ModerationDb,
                configureDbContextOptions: opt => opt.AddInterceptors(new AuditInterceptor()),
                configureSettings: settings => { settings.DisableRetry = true; }
            );
        }
        else
        {
            var connectionString = services.AddValidateOptions<PostgresCpOptions>().ConnectionString;

            services.AddDatabase<ModerationDbContext>(
                connectionString,
                opt => opt.AddInterceptors(new AuditInterceptor())
            );
        }

        services.AddScoped<IUnitOfWork, UnitOfWork<ModerationDbContext>>();

        services.AddScoped<IQueryModerationReviewRepository, QueryModerationReviewRepository>();
        services.AddScoped<IQueryModerationShopRepository, QueryModerationShopRepository>();
        services.AddScoped<IModerationShopRepository, ModerationShopRepository>();
        services.AddScoped<IModerationReviewRepository, ModerationReviewRepository>();
        services.AddScoped<IAdminModerationStatsQueryRepository, AdminModerationStatsQueryRepository>();
        services.AddScoped<IModerationAuditLogRepository, ModerationAuditLogRepository>();
        services.AddScoped<IShopImportCandidateRepository, ShopImportCandidateRepository>();
        services.AddScoped<IShopImportDuplicateSuggestionRepository, ShopImportDuplicateSuggestionRepository>();
        services.AddScoped<IShopIssueReportRepository, ShopIssueReportRepository>();
        services.AddScoped<IShopChangeRequestRepository, ShopChangeRequestRepository>();
        services.AddScoped<IQueryModerationRoasterRepository, QueryModerationRoasterRepository>();
        services.AddScoped<IModerationRoasterRepository, ModerationRoasterRepository>();

        return services;
    }
}
