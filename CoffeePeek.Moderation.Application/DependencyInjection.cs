using CoffeePeek.Moderation.Application.Features.Review.SendReviewToModeration;
using CoffeePeek.Moderation.Application.Features.Review.UpdateCoffeeShopReview;
using CoffeePeek.Moderation.Application.Features.Shop.CreateShop;
using CoffeePeek.Moderation.Application.Mapper;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Validation;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<MediaPublicUrlOptions>()
            .BindConfiguration(nameof(MediaPublicUrlOptions))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IMapper>(sp =>
            MapsterConfiguration.CreateMapper(sp.GetRequiredService<IOptions<MediaPublicUrlOptions>>().Value));

        // Validation
        services.AddTransient<IAsyncValidationStrategy<SendReviewToModerationCommand>, SendReviewToModerationValidationStrategy>();
        services.AddTransient<IValidationStrategy<UpdateCoffeeShopReviewCommand>, ReviewUpdateValidationStrategy>();

        // Application Services
        services.AddScoped<Services.PublicModerationService>();
        services.AddScoped<IModerationShopCreationService, ModerationShopCreationService>();

        return services;
    }
}
