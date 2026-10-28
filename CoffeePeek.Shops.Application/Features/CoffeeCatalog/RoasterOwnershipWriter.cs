using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed class RoasterOwnershipWriter(IRoasterRepository repository) : IRoasterOwnershipWriter
{
    public async Task<Guid?> AssignAsync(Guid roasterId, Guid? ownerUserId, bool requireUnowned, CancellationToken ct)
    {
        var roaster = await repository.GetByIdAsync(roasterId, ct)
            ?? throw new NotFoundException("Roaster not found.");
        if (requireUnowned && roaster.OwnerUserId.HasValue)
            throw new ConflictException("Roaster already has an owner.", statusCode: 409);

        var previousOwner = roaster.OwnerUserId;
        roaster.AssignOwner(ownerUserId);
        return previousOwner;
    }
}
