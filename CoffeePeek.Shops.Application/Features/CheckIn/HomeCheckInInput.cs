using System.Globalization;
using System.Text.RegularExpressions;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

internal static class HomeCheckInInput
{
    private const string OffsetTimestampPattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$";

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

    public static async Task<(Guid? CoffeeProductId, string? BeanName)> ResolveBeanAsync(
        string? productSlug, string? customBeanName, IQueryCheckInRepository repository, CancellationToken ct)
    {
        productSlug = string.IsNullOrWhiteSpace(productSlug) ? null : productSlug.Trim().ToLowerInvariant();
        customBeanName = string.IsNullOrWhiteSpace(customBeanName) ? null : customBeanName.Trim();
        if (productSlug != null && customBeanName != null)
            throw new ValidationException("CoffeeProductSlug and CustomBeanName cannot both be supplied.");
        if (productSlug == null) return (null, customBeanName);
        if (productSlug.Length > 250) throw new ValidationException("CoffeeProductSlug is too long.");
        var product = await repository.GetPublishedCoffeeProductBySlugAsync(productSlug, ct);
        if (product == null) throw new NotFoundException("Published coffee product not found.");
        return (product.Value.Id, product.Value.Name);
    }

    public static async Task<(Guid? CoffeeProductId, string? BeanName)> ResolveUpdateBeanAsync(
        Guid? unchangedProductId, string? productSlug, string? customBeanName, DomainCheckIn existing,
        IQueryCheckInRepository repository, CancellationToken ct)
    {
        if (unchangedProductId.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(productSlug) || !string.IsNullOrWhiteSpace(customBeanName))
                throw new ValidationException("CoffeeProductId cannot be combined with CoffeeProductSlug or CustomBeanName.");
            if (unchangedProductId.Value == Guid.Empty || unchangedProductId != existing.CoffeeProductId)
                throw new ValidationException("CoffeeProductId must match this entry's selected product.");
            return (existing.CoffeeProductId, existing.BeanNameSnapshot);
        }

        var selected = await ResolveBeanAsync(productSlug, customBeanName, repository, ct);
        return selected.CoffeeProductId.HasValue && selected.CoffeeProductId == existing.CoffeeProductId
            ? (existing.CoffeeProductId, existing.BeanNameSnapshot) : selected;
    }
}
