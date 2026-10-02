using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public sealed partial class Review : Entity<Guid>
{
    public string? DrinkSlug { get; private set; }
    public string? CustomDrinkName { get; private set; }
    public string? DrinkNameRu { get; private set; }
    public string? DrinkNameEn { get; private set; }

    public void SetDrink(CoffeePeek.Shared.Kernel.Drinks.DrinkSelection? drink)
    {
        DrinkSlug = drink?.Slug;
        CustomDrinkName = drink?.CustomName;
        DrinkNameRu = drink?.NameRu;
        DrinkNameEn = drink?.NameEn;
    }

    public string? Header { get; private set; }
    public string Comment { get; private set; }
    public Guid CoffeeShopId { get; private set; }
    
    public Guid UserId { get; private set; }
    public string UserName { get; private set; }

    public bool IsSoftDelete { get; private set; }
    public bool IsRemovedByAdmin { get; private set; }
    public uint Version { get; private set; }
    public int HelpfulCount { get; private set; }

    public Guid? ModerationReviewId { get; private set; }

    public Rating Rating { get; private set; }
    
    private readonly List<ShopPhoto> _shopPhotos = [];
    public IReadOnlyCollection<ShopPhoto> Photos => _shopPhotos.AsReadOnly();

    // ReSharper disable once UnusedMember.Local
    private Review()
    {
    }

    private Review(Guid coffeeShopId, Guid userId, string userName, string? header, string comment)
    {
        Id = Guid.NewGuid();
        CoffeeShopId = coffeeShopId;
        UserId = userId;
        UserName = userName;
        Header = header;
        Comment = comment;
    }

    private Review(Guid coffeeShopId, Guid userId, string userName, string? header, string comment, Rating rating, Guid? moderationReviewId)
        : this(coffeeShopId, userId, userName, header, comment)
    {
        Rating = rating;
        ModerationReviewId = moderationReviewId;
    }
}
