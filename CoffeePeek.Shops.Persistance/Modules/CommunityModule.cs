using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Application.Features.Profiles;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoffeePeek.Shops.Persistance.Modules;

public static class CommunityModule
{
    public static IServiceCollection AddCommunityModule(this IServiceCollection services)
    {
        services.AddScoped<ICheckInQueries, CheckInQueries>();
        services.AddScoped<ICheckInPhotoAccessQueries, CheckInPhotoAccessQueries>();
        services.AddScoped<ICheckInPhotoVariantProjection, Community.CheckInPhotoVariantProjection>();
        services.AddOptions<CheckInPhotoDeliveryOptions>().BindConfiguration(nameof(CheckInPhotoDeliveryOptions))
            .Validate(o => !o.AdvertiseVariants || o.ServeVariants, "Advertising variants requires serving them.").ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IProfileActivityQueries, ProfileActivityQueries>();
        services.AddScoped<ICheckInFeedReader, CheckInFeedReader>();
        services.AddScoped<IQueryCheckInRepository, QueryCheckInRepository>();
        services.AddScoped<IReviewReportRepository, ReviewReportRepository>();
        return services;
    }
}
