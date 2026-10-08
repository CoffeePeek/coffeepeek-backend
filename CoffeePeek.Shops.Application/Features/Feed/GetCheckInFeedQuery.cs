using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.Features.Feed;

public sealed record GetCheckInFeedQuery(Guid? ReaderId, string? CitySlug = null,
    string? CoffeeShopSlug = null, string? AuthorSlug = null, int PageSize = 20, string? Cursor = null)
{
    public void Validate()
    {
        if (PageSize is < 1 or > 100)
            throw new ValidationException("PageSize must be between 1 and 100.");
        foreach (var slug in new[] { CitySlug, CoffeeShopSlug, AuthorSlug })
            if (slug is not null && (string.IsNullOrWhiteSpace(slug) || slug.Length > 100))
                throw new ValidationException("Feed filter slugs must contain 1 to 100 characters.");
        if (Cursor is not null && (Cursor.Length is 0 or > 512))
            throw new ValidationException("Invalid feed cursor.");
    }
}
