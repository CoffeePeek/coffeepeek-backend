using System.Text.RegularExpressions;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Domain.Entities.UserAggregate;

public partial record Username
{
    private const int MinLength = 3;
    private const int MaxLength = BusinessConstants.MaxUserNameLength;

    private static readonly Regex UsernameRegex = MyRegex();

    public string Value { get; init; }

    private Username(string value)
    {
        Value = value;
    }

    public static Username Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Username cannot be empty.");

        value = value.Trim();

        if (value.Length is < MinLength or > MaxLength)
            throw new DomainException($"Username must be between {MinLength} and {MaxLength} characters.");

        if (!UsernameRegex.IsMatch(value))
            throw new DomainException(
                "Username can only contain letters, numbers, dots, and underscores, and must start with a letter.");
        
        return new Username(value);
    }

    public static implicit operator string(Username username) => username.Value;

    public override string ToString() => Value;

    [GeneratedRegex(@"^\p{L}[\p{L}\p{Nd}._]*$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
