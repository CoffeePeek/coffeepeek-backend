using CoffeePeek.Contract.Enums;
using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Menu;

namespace CoffeePeek.Contract.Dtos.Menu;

public record UpdateShopMenuItemRequest(
    string Slug,
    MenuItemAvailability Availability,
    decimal? Price) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Slug))
            return [new ValidationResult("Menu item slug is required.", [nameof(Slug)])];
        try
        {
            MenuItemValues.Validate((int)Availability, Price);
            return [];
        }
        catch (DomainException ex)
        {
            return [new ValidationResult(ex.Message, [nameof(Availability), nameof(Price)])];
        }
    }
}
