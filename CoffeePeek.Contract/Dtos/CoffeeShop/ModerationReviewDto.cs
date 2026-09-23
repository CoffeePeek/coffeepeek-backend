using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ModerationReviewDto
{
    public Guid Id { get; init; }

    public string? Header { get; init; }
    public string Comment { get;init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; }
    public Guid ShopId { get;init; }

    public RatingDto Rating { get; init; }

    public string? RejectedReason  { get; init; }
    public Guid? ModeratedBy  { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ModeratedAt  { get; init; }
    public ModerationStatus ModerationStatus { get; init; }
    public PhotoMetadataDto[] Photos { get; init; } = [];
}
