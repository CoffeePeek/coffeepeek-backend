using System.Globalization;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using System.Text.RegularExpressions;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

internal static class HomeCheckInInput
{
    private const string OffsetTimestampPattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$";

    public static async Task<string?> ResolveBrewMethodAsync(string? code, IPublicAddressService addresses,
        CancellationToken ct, string? existingCode = null)
    {
        if (code is null) return null;
        if (!HomeBrewMethodCode.IsValid(code))
            throw new ValidationException("Invalid home brew method.");
        // Existing snapshots and old clients remain editable after a catalog rename/removal.
        if (code == existingCode || HomeBrewMethodCode.IsLegacy(code)) return code;
        var address = await addresses.ResolveAsync(PublicAddressKind.BrewMethod, code, ct);
        return address?.Slug ?? throw new ValidationException("Brew method not found in the catalog.");
    }

    public static (DateTime PreparedAtUtc, DateOnly PreparedOn) ParseTime(string? preparedAt, DateOnly? preparedOn)
    {
        if (string.IsNullOrWhiteSpace(preparedAt)
            || !Regex.IsMatch(preparedAt, OffsetTimestampPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            || !DateTimeOffset.TryParse(preparedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            throw new ValidationException("PreparedAt must be an ISO 8601 timestamp with an explicit UTC offset or Z.");

        var localDate = preparedOn ?? DateOnly.FromDateTime(timestamp.Date);
        var utcDate = DateOnly.FromDateTime(timestamp.UtcDateTime);
        if (Math.Abs(localDate.DayNumber - utcDate.DayNumber) > 1)
            throw new ValidationException("PreparedOn must be within one calendar day of PreparedAt in UTC.");
        return (timestamp.UtcDateTime, localDate);
    }

    /// <summary>
    /// One of: a published catalog product by slug, a custom name, or the author's own shelf bag. A bag supplies its
    /// product or custom name and its roaster; another owner's bag is indistinguishable from a missing one.
    /// </summary>
    public static async Task<HomeCheckInBean> ResolveBeanAsync(Guid userId, string? productSlug, string? customBeanName,
        Guid? shelfBagId, IQueryCheckInRepository repository, CancellationToken ct)
    {
        productSlug = string.IsNullOrWhiteSpace(productSlug) ? null : productSlug.Trim().ToLowerInvariant();
        customBeanName = string.IsNullOrWhiteSpace(customBeanName) ? null : customBeanName.Trim();
        if (productSlug != null && customBeanName != null)
            throw new ValidationException("CoffeeProductSlug and CustomBeanName cannot both be supplied.");
        if (shelfBagId.HasValue)
        {
            if (productSlug != null || customBeanName != null)
                throw new ValidationException("ShelfBagId cannot be combined with CoffeeProductSlug or CustomBeanName.");
            if (shelfBagId.Value == Guid.Empty) throw new ValidationException("ShelfBagId must not be empty.");
            var bag = await repository.GetOwnedShelfBagBeanAsync(userId, shelfBagId.Value, ct)
                ?? throw new NotFoundException("Shelf bag not found.");
            return new HomeCheckInBean(bag.CoffeeProductId, bag.Name, bag.RoasterName, shelfBagId, bag.CardPhotoId);
        }
        if (productSlug == null) return new HomeCheckInBean(null, customBeanName, null, null);
        if (productSlug.Length > 250) throw new ValidationException("CoffeeProductSlug is too long.");
        var product = await repository.GetPublishedCoffeeProductBySlugAsync(productSlug, ct);
        if (product == null) throw new NotFoundException("Published coffee product not found.");
        return new HomeCheckInBean(product.Value.Id, product.Value.Name, product.Value.RoasterName, null);
    }

    /// <summary>
    /// An unchanged <paramref name="shelfBagId"/> or <paramref name="unchangedProductId"/> keeps this entry's snapshots,
    /// even after the bag was edited or deleted or the product unpublished. Without a bag the link is cleared.
    /// </summary>
    public static async Task<HomeCheckInBean> ResolveUpdateBeanAsync(Guid userId, Guid? unchangedProductId,
        string? productSlug, string? customBeanName, Guid? shelfBagId, DomainCheckIn existing,
        IQueryCheckInRepository repository, CancellationToken ct)
    {
        var kept = new HomeCheckInBean(existing.CoffeeProductId, existing.BeanNameSnapshot,
            existing.RoasterNameSnapshot, existing.ShelfBagId);
        if (shelfBagId.HasValue && shelfBagId == existing.ShelfBagId)
        {
            if (unchangedProductId.HasValue || !string.IsNullOrWhiteSpace(productSlug) || !string.IsNullOrWhiteSpace(customBeanName))
                throw new ValidationException("ShelfBagId cannot be combined with another bean selector.");
            return kept;
        }
        if (unchangedProductId.HasValue)
        {
            if (shelfBagId.HasValue || !string.IsNullOrWhiteSpace(productSlug) || !string.IsNullOrWhiteSpace(customBeanName))
                throw new ValidationException("CoffeeProductId cannot be combined with CoffeeProductSlug, CustomBeanName or ShelfBagId.");
            if (unchangedProductId.Value == Guid.Empty || unchangedProductId != existing.CoffeeProductId)
                throw new ValidationException("CoffeeProductId must match this entry's selected product.");
            return kept with { ShelfBagId = null };
        }

        var selected = await ResolveBeanAsync(userId, productSlug, customBeanName, shelfBagId, repository, ct);
        return selected.ShelfBagId == null && selected.CoffeeProductId.HasValue && selected.CoffeeProductId == existing.CoffeeProductId
            ? kept with { ShelfBagId = null } : selected;
    }
}
