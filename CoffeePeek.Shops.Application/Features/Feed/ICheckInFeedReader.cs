namespace CoffeePeek.Shops.Application.Features.Feed;

// A read model of published check-ins, independent of the persistence technology.
public interface ICheckInFeedReader
{
    Task<CheckInFeedReadPage> ReadAsync(CheckInFeedScope scope, int pageSize,
        CheckInFeedPosition? after, Guid? readerId, CancellationToken ct);
}
