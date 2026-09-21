using CoffeePeek.Contract.Dtos;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using Mapster;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Mapper;

public partial class MapsterConfiguration
{
    public static IMapper CreateMapper(MediaPublicUrlOptions mediaOptions)
    {
        var config = Configure(mediaOptions);
        return new MapsterMapper.Mapper(config);
    }

    private static TypeAdapterConfig Configure(MediaPublicUrlOptions mediaOptions)
    {
        var config = new TypeAdapterConfig();

        config.NewConfig<PhotoMetadata, ShortPhotoMetadataDto>()
            .Map(d => d.FullUrl, s =>
                MediaStorageUrlBuilder.BuildPublicUrl(
                    mediaOptions.PublicEndpoint,
                    mediaOptions.ShopBucketName,
                    s.StorageKey) ?? string.Empty);

        config.NewConfig<PhotoMetadata, PhotoMetadataDto>()
            .Map(d => d.FullUrl, s =>
                MediaStorageUrlBuilder.BuildPublicUrl(
                    mediaOptions.PublicEndpoint,
                    mediaOptions.ShopBucketName,
                    s.StorageKey));

        ConfigureModerationReview(config);
        ConfigureModerationShop(config, mediaOptions);
        ConfigureModerationRoaster(config, mediaOptions);

        return config;
    }
}
