namespace CoffeePeek.Contract.Events.Shops;

public record CheckinCreatedEvent
{
    public Guid UserId { get; init; }
    public Guid ShopId { get; init; }
    public DateTime CreatedAt { get; init; }
    public Guid CheckInId { get; init; }
}
