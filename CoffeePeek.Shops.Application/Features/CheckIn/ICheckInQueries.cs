using CoffeePeek.Contract.Dtos.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

public interface ICheckInQueries
{
    Task<(CheckInDto[] Items, int TotalCount)> GetByUserId(
        Guid userId,
        int pageNumber,
        int pageSize,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);
}
