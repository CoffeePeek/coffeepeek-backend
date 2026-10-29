namespace CoffeePeek.Shops.Application.Features.CheckIn;

/// <summary>
/// The resolved bean of a home check-in and where it came from. <see cref="CardPhotoId"/> is the bag's current card when
/// the bag was looked up; a kept bag link is not looked up again.
/// </summary>
internal sealed record HomeCheckInBean(Guid? CoffeeProductId, string? BeanName, string? RoasterName, Guid? ShelfBagId,
    Guid? CardPhotoId = null);
