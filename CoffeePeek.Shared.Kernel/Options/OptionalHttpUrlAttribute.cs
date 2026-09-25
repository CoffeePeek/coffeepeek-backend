using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Shared.Kernel.Options;

/// <summary>
/// Validates that a string option is either empty (null, "" or whitespace) or an absolute http(s) URL.
/// Use it instead of <see cref="UrlAttribute"/> for optional URL settings: <see cref="UrlAttribute"/>
/// only lets <c>null</c> through and rejects an empty string.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OptionalHttpUrlAttribute : ValidationAttribute
{
    public OptionalHttpUrlAttribute()
        : base("The {0} field must be empty or an absolute http(s) URL.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not string text)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
