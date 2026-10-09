using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Wolverine;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class UpdateUnifiedCheckInHandler
{
    public static async Task<Response<UnifiedCheckInDto>> Handle(UpdateUnifiedCheckInCommand command,
        IQueryCheckInRepository repository, CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog drinks, IUploadedPhotoLookup photos, IUnitOfWork unitOfWork,
        IMessageBus bus, CancellationToken ct)
    {
        var checkIn = await GetOwnedAsync(repository, command.Id, command.UserId, ct);
        if (command.Request.Rating == null) throw new ValidationException("Rating is required.");
        var drink = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync(command.Request.DrinkSlug, command.Request.CustomDrinkName, drinks, ct);
        var rating = command.Request.Rating;
        if (checkIn.Visibility == CheckInVisibility.Public) await EnsurePhotoBindingsAsync(checkIn,photos,ct);
        checkIn.UpdateContent(command.Request.Text, Rating.Create(rating.Place, rating.Service, rating.Coffee));
        checkIn.SetDrink(drink);
        return await SaveAsync(checkIn, unitOfWork, bus, ct);
    }

    public static async Task<Response<UnifiedCheckInDto>> Handle(ChangeUnifiedCheckInVisibilityCommand command,
        IQueryCheckInRepository repository, IUploadedPhotoLookup photos, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var checkIn = await GetOwnedAsync(repository, command.Id, command.UserId, ct);
        var revision = checkIn.ContentRevision;
        if ((CheckInVisibility)command.Visibility == CheckInVisibility.Public) await EnsurePhotoBindingsAsync(checkIn,photos,ct);
        checkIn.ChangeVisibility((CheckInVisibility)command.Visibility);
        if (revision == checkIn.ContentRevision) return Response<UnifiedCheckInDto>.Success(CheckInMapping.ToDto(checkIn));
        return await SaveAsync(checkIn, unitOfWork, bus, ct);
    }

    public static async Task<Response> Handle(DeleteUnifiedCheckInCommand command, IQueryCheckInRepository repository,
        IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var checkIn = await GetOwnedAsync(repository, command.Id, command.UserId, ct);
        checkIn.Delete();
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id, checkIn.ShopId, checkIn.UserId));
        return Response.Success();
    }

    internal static async Task<DomainCheckIn> GetOwnedAsync(IQueryCheckInRepository repository, Guid id, Guid userId, CancellationToken ct)
    {
        var checkIn = await repository.GetTrackedByIdAsync(id, ct);
        if (checkIn == null || checkIn.UserId != userId || checkIn.DeletedAtUtc != null)
            throw new NotFoundException("Check-in not found.");
        return checkIn;
    }

    private static async Task EnsurePhotoBindingsAsync(DomainCheckIn checkIn,IUploadedPhotoLookup photos,CancellationToken ct)
    {
        if(checkIn.ShopPhotos.Count==0)return;
        var claim=await photos.ClaimForCheckInAsync(checkIn.UserId,checkIn.Id,checkIn.ShopPhotos.Select(p=>p.StorageKey).ToArray(),ct);
        if(!claim.Succeeded || claim.Photos.Count!=checkIn.ShopPhotos.Count || checkIn.ShopPhotos.Any(p=>
            !claim.Photos.Any(bound=>bound.PhotoId==p.Id && bound.StorageKey==p.StorageKey)))
            throw new ConflictException("Photo ownership and check-in binding could not be verified.");
    }

    private static async Task<Response<UnifiedCheckInDto>> SaveAsync(DomainCheckIn checkIn, IUnitOfWork unitOfWork,
        IMessageBus bus, CancellationToken ct)
    {
        if (checkIn.Visibility == CheckInVisibility.Public) await bus.PublishAsync(CheckInMapping.ModerationRequest(checkIn));
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id, checkIn.ShopId, checkIn.UserId));
        return Response<UnifiedCheckInDto>.Success(CheckInMapping.ToDto(checkIn));
    }
}
