namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record PhotoBackfillCursor(DateTime SortAtUtc, Guid CheckInId, Guid PhotoId);
