using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Feed;

public sealed record CheckInFeedItemDto(DateTime PublishedAtUtc, PublicCheckInDto CheckIn);
