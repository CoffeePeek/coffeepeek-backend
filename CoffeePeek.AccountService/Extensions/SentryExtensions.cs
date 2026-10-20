using CoffeePeek.Shared.Kernel.Exceptions;
using Sentry;

namespace CoffeePeek.AccountService.Extensions;

internal static class SentryExtensions
{
    /// <summary>
    /// Drops expected client/auth failures from Sentry — they are handled and returned as 4xx responses.
    /// </summary>
    public static SentryEvent? FilterExpectedClientErrors(SentryEvent sentryEvent, SentryHint hint)
    {
        switch (sentryEvent.Exception)
        {
            case ConflictException: // Defaults to no status code, unlike the other 4xx exceptions.
                return null;
            case BaseException { StatusCode: >= 400 and < 500 }:
                return null;
            case DomainException domain when domain.Message.Contains("Security breach", StringComparison.Ordinal):
                return null;
        }

        return sentryEvent;
    }
}
