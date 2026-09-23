using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CoffeePeek.Shops.Application.Services;

public class CreateRoasterFromModerationService(
    IQueryRoasterRepository queryRoasterRepository,
    IRoasterRepository roasterRepository,
    IUnitOfWork unitOfWork,
    ICacheService cacheService,
    ILogger<CreateRoasterFromModerationService> logger) : ICreateRoasterFromModerationService
{
    public async Task<Guid> CreateRoasterFromApprovedEventAsync(
        ModerationRoasterDto roasterDto, Guid moderationId, CancellationToken ct = default)
    {
        var existingId = await queryRoasterRepository.GetIdByModerationId(moderationId, ct);
        if (existingId.HasValue)
        {
            logger.LogInformation(
                "Roaster {RoasterId} already exists for moderation {ModerationId}",
                existingId.Value,
                moderationId);
            return existingId.Value;
        }

        var roaster = new Roaster(roasterDto.Name, moderationId);
        roaster.SetAbout(roasterDto.About);

        if (roasterDto.Location is not null && roasterDto.CityId.HasValue)
        {
            var location = roasterDto.Location.Latitude.HasValue && roasterDto.Location.Longitude.HasValue
                ? Location.CreateValidated(
                    roasterDto.CityId.Value,
                    roasterDto.Location.Address,
                    roasterDto.Location.Latitude.Value,
                    roasterDto.Location.Longitude.Value)
                : Location.CreateDraft(roasterDto.CityId.Value, roasterDto.Location.Address);
            roaster.SetLocation(location);
        }

        if (roasterDto.Contact is not null)
            roaster.SetContact(RoasterContact.Create(roasterDto.Contact.InstagramLink, roasterDto.Contact.SiteLink));

        if (roasterDto.Photos is { Length: > 0 })
        {
            var photos = roasterDto.Photos.Select(p => new RoasterPhoto(
                p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.OwnerId, p.SortIndex));
            roaster.ReplacePhotos(photos);
        }

        roasterRepository.Add(roaster);
        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveByPattern(CacheKey.Roaster.ListPattern(), ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        logger.LogInformation(
            "Roaster {RoasterId} successfully created from moderation event {ModerationId}",
            roaster.Id,
            moderationId);

        return roaster.Id;
    }
}
