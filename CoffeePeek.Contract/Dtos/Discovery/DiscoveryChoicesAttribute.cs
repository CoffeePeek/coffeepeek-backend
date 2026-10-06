using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Contract.Dtos.Discovery;

[AttributeUsage(AttributeTargets.Property)]
public sealed class DiscoveryChoicesAttribute(params string[] choices) : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null || (value is string s
        ? choices.Contains(s, StringComparer.Ordinal)
        : value is string[] items && items.All(s => choices.Contains(s, StringComparer.Ordinal)));
}
