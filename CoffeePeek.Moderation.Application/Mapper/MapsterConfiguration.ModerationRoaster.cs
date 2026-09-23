using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using Mapster;

namespace CoffeePeek.Moderation.Application.Mapper;

public partial class MapsterConfiguration
{
    private static void ConfigureModerationRoaster(TypeAdapterConfig config, MediaPublicUrlOptions mediaOptions)
    {
        config.NewConfig<ModerationRoasterPhoto, PhotoMetadataDto>()
            .Map(d => d.FullUrl, s =>
                MediaStorageUrlBuilder.BuildPublicUrl(
                    mediaOptions.PublicEndpoint,
                    mediaOptions.ShopBucketName,
                    s.StorageKey));

        config.NewConfig<ModerationRoaster, ModerationRoasterDto>();
    }
}
