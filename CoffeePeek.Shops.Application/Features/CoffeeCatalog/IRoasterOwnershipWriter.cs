namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public interface IRoasterOwnershipWriter
{
    Task<Guid?> AssignAsync(Guid roasterId, Guid? ownerUserId, bool requireUnowned, CancellationToken ct);
}
