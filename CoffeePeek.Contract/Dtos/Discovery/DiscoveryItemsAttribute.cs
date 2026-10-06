using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Contract.Dtos.Discovery;

[AttributeUsage(AttributeTargets.Property)]
public sealed class DiscoveryItemsAttribute(int maximum, int minimum = 0, bool unique = false) : ValidationAttribute
{
    public override bool IsValid(object? value) => value is Array a && a.Length >= minimum && a.Length <= maximum
        && a.Cast<object?>().All(v => v is not null)
        && (!unique || a.Cast<object?>().Distinct().Count() == a.Length);
}
