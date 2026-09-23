namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public class CheckInDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ShopId { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime VisitedAt { get; set; }
    public Guid? ReviewId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public RatingDto Rating { get; set; } = new();
    public ShortPhotoMetadataDto[] Photos { get; set; } = [];
}


