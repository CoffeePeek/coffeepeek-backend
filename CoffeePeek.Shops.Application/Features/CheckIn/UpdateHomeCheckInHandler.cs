using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Wolverine;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class UpdateHomeCheckInHandler
{
    public static async Task<Response<HomeCheckInDto>> Handle(UpdateHomeCheckInCommand command,
        IQueryCheckInRepository repository, CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog drinks, IPublicAddressService addresses,
        IUploadedPhotoLookup photos, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var checkIn = await repository.GetTrackedByIdAsync(command.Id, ct);
        if (checkIn == null || checkIn.Kind != CheckInKind.Home || checkIn.UserId != command.UserId
            || checkIn.DeletedAtUtc != null)
            throw new NotFoundException("Home check-in not found.");

        var request = command.Request;
        var (preparedAtUtc, preparedOn) = HomeCheckInInput.ParseTime(request.PreparedAt, request.PreparedOn);
        var bean = await HomeCheckInInput.ResolveUpdateBeanAsync(command.UserId, request.CoffeeProductId,
            request.CoffeeProductSlug, request.CustomBeanName, request.ShelfBagId, checkIn, repository, ct);
        var brewMethodCode = await HomeCheckInInput.ResolveBrewMethodAsync(request.BrewMethodCode, addresses, ct, checkIn.BrewMethodCode);
        var drink = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync(
            request.DrinkSlug, request.CustomDrinkName, drinks, ct);
        checkIn.UpdateHome(request.Text, preparedAtUtc, preparedOn, brewMethodCode, bean.CoffeeProductId,
            bean.BeanName, request.DoseGrams, request.BrewDurationSeconds, request.TasteScore,
            request.Visibility.HasValue ? (CheckInVisibility)request.Visibility.Value : null);
        var keepCard = request.ShowShelfBag && checkIn.ShelfBagPhotoId != null && bean.ShelfBagId == checkIn.ShelfBagId;
        var hiddenCard = keepCard ? null : checkIn.DetachShelfBagPhoto();
        checkIn.SetBeanSource(bean.ShelfBagId, bean.RoasterName);
        var shownCard = keepCard || !request.ShowShelfBag ? null
            : await AttachBagCardAsync(checkIn, bean, repository, photos, ct);
        checkIn.SetDrink(drink);
        if (hiddenCard is { } hidden)
            await bus.PublishAsync(new CheckInPhotoDetachedEvent(checkIn.UserId, checkIn.Id, hidden, DateTime.UtcNow));
        if (shownCard != null)
            await bus.PublishAsync(new CheckInPhotosAttachedEvent(checkIn.UserId, checkIn.Id, [shownCard], DateTime.UtcNow));
        if (checkIn.Visibility == CheckInVisibility.Public)
            await bus.PublishAsync(CheckInMapping.ModerationRequest(checkIn));
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id, null, checkIn.UserId));
        await unitOfWork.SaveChangesAsync(ct);
        return Response<HomeCheckInDto>.Success(HomeCheckInMapping.ToDto(checkIn));
    }

    /// <summary>A kept bag link is not looked up during bean resolution, so its current card is read here.</summary>
    private static async Task<string?> AttachBagCardAsync(DomainCheckIn checkIn, HomeCheckInBean bean,
        IQueryCheckInRepository repository, IUploadedPhotoLookup photos, CancellationToken ct)
    {
        if (bean.ShelfBagId is not { } bagId) return null;
        var cardPhotoId = bean.CardPhotoId
            ?? (await repository.GetOwnedShelfBagBeanAsync(checkIn.UserId, bagId, ct))?.CardPhotoId;
        return cardPhotoId is { } card ? await HomeCheckInBagCard.AttachAsync(checkIn, bagId, card, photos, ct) : null;
    }

    public static async Task<Response> Handle(DeleteHomeCheckInCommand command,
        IQueryCheckInRepository repository, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var checkIn = await repository.GetTrackedByIdAsync(command.Id, ct);
        if (checkIn == null || checkIn.Kind != CheckInKind.Home || checkIn.UserId != command.UserId
            || checkIn.DeletedAtUtc != null)
            throw new NotFoundException("Home check-in not found.");
        checkIn.Delete();
        await bus.PublishAsync(new CoffeePeek.Contract.Events.CheckInPhotosDeletedEvent(
            checkIn.Id, checkIn.UserId, checkIn.DeletedAtUtc!.Value));
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id, null, checkIn.UserId));
        await unitOfWork.SaveChangesAsync(ct);
        return Response.Success();
    }
}
