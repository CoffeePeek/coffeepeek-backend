using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public interface IPublicCheckInProjector
{
    Task<PublicCheckInDto[]> ProjectAsync(UnifiedCheckInDto[] items, CancellationToken ct);
}
