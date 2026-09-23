using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shared.Persistence.Extensions;

/// <summary>
/// Database failures worth re-running a handler for: the competing write has committed and a retry sees it
/// (idempotency checks, optimistic concurrency). Permanent errors — FK/check violations, overflow, too-long
/// strings — are excluded: retrying them only repeats side effects such as paid OpenAI calls.
/// </summary>
public static class TransientDbConflict
{
    private const string UniqueViolation = "23505";
    private const string SerializationFailure = "40001";

    public static bool Matches(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is DbUpdateConcurrencyException)
                return true;
            if (ex is PostgresException { SqlState: UniqueViolation or SerializationFailure })
                return true;
        }

        return false;
    }
}
