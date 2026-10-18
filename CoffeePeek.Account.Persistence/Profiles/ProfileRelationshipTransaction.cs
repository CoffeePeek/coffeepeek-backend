using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Account.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Persistence.Profiles;

// Follow and block writes share a pair lock, so a racing follow cannot survive a block.
internal static class ProfileRelationshipTransaction
{
    public static async Task RunAsync(AccountDbContext db, Guid first, Guid second, Func<Task> write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) { await write(); return; }
        var pair = new[] { first.ToString("N"), second.ToString("N") }.Order(StringComparer.Ordinal);
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(":", pair))));
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        await write();
        if (transaction is not null) await transaction.CommitAsync(ct);
    }
}
