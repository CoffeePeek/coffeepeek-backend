using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class UpdateHomeCheckInHandler
{
    public static async Task<Response<HomeCheckInDto>> Handle(UpdateHomeCheckInCommand command,
        IQueryCheckInRepository repository, CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog drinks, IPublicAddressService addresses,
        IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        var checkIn = await repository.GetTrackedByIdAsync(command.Id, ct);
        if (checkIn == null || checkIn.Kind != CheckInKind.Home || checkIn.UserId != command.UserId
            || checkIn.DeletedAtUtc != null)
            throw new NotFoundException("Home check-in not found.");

        var request = command.Request;
        var (preparedAtUtc, preparedOn) = HomeCheckInInput.ParseTime(request.PreparedAt, request.PreparedOn);
        var (coffeeProductId, beanName) = await HomeCheckInInput.ResolveUpdateBeanAsync(
            request.CoffeeProductId, request.CoffeeProductSlug, request.CustomBeanName, checkIn, repository, ct);
        var brewMethodCode = await HomeCheckInInput.ResolveBrewMethodAsync(request.BrewMethodCode, addresses, ct, checkIn.BrewMethodCode);
        var drink = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync(
            request.DrinkSlug, request.CustomDrinkName, drinks, ct);
        checkIn.UpdateHome(request.Text, preparedAtUtc, preparedOn, brewMethodCode, coffeeProductId,
            beanName, request.DoseGrams, request.BrewDurationSeconds, request.TasteScore,
            request.Visibility.HasValue ? (CheckInVisibility)request.Visibility.Value : null);
        checkIn.SetDrink(drink);
        if (checkIn.Visibility == CheckInVisibility.Public)
            await bus.PublishAsync(CheckInMapping.ModerationRequest(checkIn));
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id, null, checkIn.UserId));
        await unitOfWork.SaveChangesAsync(ct);
        return Response<HomeCheckInDto>.Success(HomeCheckInMapping.ToDto(checkIn));
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
