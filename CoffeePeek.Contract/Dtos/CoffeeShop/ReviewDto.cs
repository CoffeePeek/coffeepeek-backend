namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ReviewDto
{
    public string? DrinkSlug { get; init; }
    public string? CustomDrinkName { get; init; }
    public string? DrinkNameRu { get; init; }
    public string? DrinkNameEn { get; init; }

    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid CoffeeShopId { get; init; }
    public string Username { get; init; }

    public string? Header { get; init; }
    public string Comment { get; init; }

    public RatingDto Rating { get; init; }

    public Guid? ModerationReviewId { get; init; }

    public ICollection<UploadedPhotoDto> Photos { get; init; } = new List<UploadedPhotoDto>();
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>Total number of "helpful" votes this review has received.</summary>
    public int HelpfulCount { get; init; }

    /// <summary>True when the current authenticated user has marked this review helpful; false otherwise.</summary>
    public bool IsHelpfulByCurrentUser { get; init; }
}
