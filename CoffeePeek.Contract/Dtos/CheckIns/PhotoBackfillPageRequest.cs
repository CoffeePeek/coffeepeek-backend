#nullable enable
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record PhotoBackfillPageRequest(PhotoBackfillPhase Phase, DateTime BeforeUtc, DateTime RecentSinceUtc,
    PhotoBackfillCursor? Cursor, int Limit = 100)
{
    public bool IsValid(DateTime utcNow) => Enum.IsDefined(Phase) && Limit is >= 1 and <= 100
        && BeforeUtc != default && BeforeUtc.Kind == DateTimeKind.Utc && BeforeUtc <= utcNow
        && RecentSinceUtc != default && RecentSinceUtc.Kind == DateTimeKind.Utc && RecentSinceUtc <= BeforeUtc
        && (Cursor is null || (Cursor.CheckInId != Guid.Empty && Cursor.PhotoId != Guid.Empty
            && Cursor.SortAtUtc != default && Cursor.SortAtUtc.Kind == DateTimeKind.Utc && Cursor.SortAtUtc <= BeforeUtc));
}
