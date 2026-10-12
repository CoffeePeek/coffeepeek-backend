using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public static class HomeCheckInMapping
{
    public static HomeCheckInDto ToDto(DomainCheckIn checkIn)
    {
        if (checkIn.Kind != CheckInKind.Home || checkIn.PreparedOn == null)
            throw new InvalidOperationException("Expected a home check-in.");
        var shared = CheckInMapping.ToDto(checkIn);
        return new HomeCheckInDto
        {
            Id = checkIn.Id,
            PreparedAtUtc = checkIn.VisitedAt,
            PreparedOn = checkIn.PreparedOn.Value,
            CreatedAtUtc = checkIn.CreatedAtUtc,
            Text = checkIn.Note,
            BrewMethodCode = checkIn.BrewMethodCode,
            CoffeeProductId = checkIn.CoffeeProductId,
            BeanName = checkIn.BeanNameSnapshot,
            DoseGrams = checkIn.DoseGrams,
            BrewDurationSeconds = checkIn.BrewDurationSeconds,
            TasteScore = checkIn.TasteScore,
            Drink = shared.Drink,
            Photos = shared.Photos,
            DrinkPhoto = shared.DrinkPhoto
        };
    }
}
