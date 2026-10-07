using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using Mapster;
using MapsterMapper;

namespace CoffeePeek.Account.Application.Mapper;

public static class MapsterConfiguration
{
    public static IMapper CreateMapper(MediaPublicUrlOptions mediaOptions)
    {
        var config = Configure(mediaOptions);
        return new MapsterMapper.Mapper(config);
    }

    private static TypeAdapterConfig Configure(MediaPublicUrlOptions mediaOptions)
    {
        var config = new TypeAdapterConfig();

        config.NewConfig<User, UserProfileResponse>()
            .Map(d => d.UserName, s => s.Username.Value)
            .Map(d => d.Email, s => s.Credentials.Email)
            .Map(dest => dest.CheckInCount, src => src.Statistics.CheckInCount)
            .Map(dest => dest.AddedShopsCount, src => src.Statistics.AddedShopsCount)
            .Map(dest => dest.AvatarUrl, src => src.PhotoMetadata != null
                ? MediaStorageUrlBuilder.BuildPublicUrl(
                    mediaOptions.PublicEndpoint,
                    mediaOptions.AvatarBucketName,
                    src.PhotoMetadata!.StorageKey)
                : null);

        config.NewConfig<User, PublicUserProfileResponse>()
            .Map(d => d.UserName, s => s.Username.Value)
            .Map(dest => dest.CheckInCount, src => src.Statistics.CheckInCount)
            .Map(dest => dest.AddedShopsCount, src => src.Statistics.AddedShopsCount)
            .Map(dest => dest.AvatarUrl, src => src.PhotoMetadata != null
                ? MediaStorageUrlBuilder.BuildPublicUrl(
                    mediaOptions.PublicEndpoint,
                    mediaOptions.AvatarBucketName,
                    src.PhotoMetadata!.StorageKey)
                : null);

        return config;
    }
}
