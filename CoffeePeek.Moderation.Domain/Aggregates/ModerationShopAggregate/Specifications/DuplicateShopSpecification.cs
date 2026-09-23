using System.Linq.Expressions;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Domain.Interfaces.Persistance;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public class DuplicateShopSpecification : ISpecification<ModerationShop>
{
    public DuplicateShopSpecification(string name, string address)
    {
        Criteria = shop =>
#pragma warning disable CA1862
            shop.Name.ToLower() == name.ToLower()
            && shop.Location != null && shop.Location.Address.ToLower() == address.ToLower()
            && shop.ModerationStatus != ModerationStatus.Rejected;
#pragma warning restore CA1862
    }

    public Expression<Func<ModerationShop, bool>> Criteria { get; }
}
