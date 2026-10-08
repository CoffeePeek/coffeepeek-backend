using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.Shops.Application.Abstractions;

public record CheckInPhotoClaimResult(bool Succeeded, IReadOnlyList<ClaimedCheckInPhotoDto> Photos)
{
    public static CheckInPhotoClaimResult Failed { get; } = new(false, []);
}
