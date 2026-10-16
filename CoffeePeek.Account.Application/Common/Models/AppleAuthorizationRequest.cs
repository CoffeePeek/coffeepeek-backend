namespace CoffeePeek.Account.Application.Common.Models;

public sealed record AppleAuthorizationRequest(string AuthorizationUrl, string State, DateTimeOffset ExpiresAt)
{
    public override string ToString() => $"AppleAuthorizationRequest {{ AuthorizationUrl = [REDACTED], State = [REDACTED], ExpiresAt = {ExpiresAt} }}";
}
