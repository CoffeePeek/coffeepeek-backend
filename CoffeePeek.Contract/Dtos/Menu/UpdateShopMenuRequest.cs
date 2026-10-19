using CoffeePeek.Contract.Enums;
using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Contract.Dtos.Menu;

public record UpdateShopMenuRequest(
    IReadOnlyList<UpdateShopMenuItemRequest> Items,
    bool ApplySuggestedPriceRange = false,
    long? ExpectedInputRevision = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Items is null || Items.Any(item => item is null))
            return [new ValidationResult("Menu items are required and cannot contain null entries.", [nameof(Items)])];
        if (Items.Select(item => item.Slug?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Items.Count)
            return [new ValidationResult("Menu item slugs must be unique.", [nameof(Items)])];
        return [];
    }
}
