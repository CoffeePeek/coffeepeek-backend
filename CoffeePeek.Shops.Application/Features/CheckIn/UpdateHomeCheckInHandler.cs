using CoffeePeek.Contract.Dtos.CheckIns;
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
        IQueryCheckInRepository repository, CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog drinks,
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
        var drink = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync(
            request.DrinkSlug, request.CustomDrinkName, drinks, ct);
        checkIn.UpdateHome(request.Text, preparedAtUtc, preparedOn, request.BrewMethodCode, coffeeProductId,
            beanName, request.DoseGrams, request.BrewDurationSeconds, request.TasteScore);
        checkIn.SetDrink(drink);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id, null, checkIn.UserId));
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
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new CheckInCacheInvalidationRequested(checkIn.Id, null, checkIn.UserId));
        return Response.Success();
    }
}
