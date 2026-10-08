namespace CoffeePeek.Contract.Dtos.Feed;

public sealed record CheckInFeedPageDto(CheckInFeedItemDto[] Items, string? NextCursor);
