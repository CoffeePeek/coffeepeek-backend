using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Moderation.Infrastructure;

public class GooglePlaces
{
    [Required] public string ApiKey { get; set; } = string.Empty;
    [Required] public string BaseUrl { get; set; } = "https://places.googleapis.com/v1/";
    [Range(1, 300)] public int TimeoutSeconds { get; set; } = 15;
    public int CacheDays { get; set; } = 30;
    public int MaxDistanceMeters { get; set; } = 250;
}
