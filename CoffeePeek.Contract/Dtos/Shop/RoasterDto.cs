namespace CoffeePeek.Contract.Dtos.Shop;

public class RoasterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    /// <summary>Public URL of the roaster's cover photo (lowest SortIndex), or null if it has none.</summary>
    public string? PhotoUrl { get; set; }

    /// <summary>Complete metadata of the roaster's cover photo, or null if it has none.</summary>
    public ShortPhotoMetadataDto? CoverPhoto { get; set; }
}
