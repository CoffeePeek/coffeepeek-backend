using CoffeePeek.Contract.Dtos.CheckIns;

namespace CoffeePeek.Shops.Application.Features.Feed;

public sealed record CheckInFeedEntry(UnifiedCheckInDto CheckIn, DateTime PublishedAtUtc);
