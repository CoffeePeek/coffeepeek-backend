using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Account.Domain.Entities.PhotoMetadataAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.PublicAddresses;
using CoffeePeek.Account.Persistence.Profiles;
using CoffeePeek.Account.Persistence.Repositories;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Extentions;
using CoffeePeek.Shared.Persistence;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shared.Persistence.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeePeek.Account.Persistence;

public static class DependencyInjection
{
    
    public static IServiceCollection AddPersistence(this IServiceCollection services, WebApplicationBuilder builder)
    {
        // Singleton options read by AccountDbContext (safe with Aspire's pooled context). Unset means disabled.
        services.Configure<PublicAddressOptions>(builder.Configuration.GetSection("PublicAddresses"));

        if (builder.Environment.IsDevelopment())
        {
            builder.AddNpgsqlDbContext<AccountDbContext>(
                connectionName: AppResources.AccountDb,
                configureDbContextOptions: opt => opt.AddInterceptors(new AuditInterceptor()),
                configureSettings: settings => { settings.DisableRetry = true; }
            );
        }
        else
        {
            var connectionString = services.AddValidateOptions<PostgresCpOptions>().ConnectionString;

            services.AddDatabase<AccountDbContext>(
                connectionString,
                opt => opt.AddInterceptors(new AuditInterceptor())
            );
        }

        services.AddScoped<IUnitOfWork, UnitOfWork<AccountDbContext>>();
        
        // 2. Repository Implementations
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IQueryUserRepository, QueryUserRepository>();
        services.AddScoped<IAdminUserQueryRepository, AdminUserQueryRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPhotoMetadataRepository, PhotoMetadataRepository>();
        services.AddScoped<IAccountDeletionRequestRepository, AccountDeletionRequestRepository>();
        services.AddScoped<IProfileAddressService, ProfileAddressService>();
        services.AddScoped<IProfileSocialStore, ProfileSocialStore>();
        services.AddScoped<IUserBlockStore, UserBlockStore>();
        services.AddScoped<IRoasterContributionStore, RoasterContributionStore>();
        services.AddScoped<CoffeePeek.Account.Application.Features.Notifications.IPushNotificationStore,
            CoffeePeek.Account.Persistence.Notifications.PushNotificationStore>();
        services.AddCacheModule();

        return services;
    }
}
