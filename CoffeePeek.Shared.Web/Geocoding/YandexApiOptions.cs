using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Shared.Web.Geocoding;

public class YandexApiOptions
{
    [Required] public string ApiKey { get; set; } = string.Empty;
    [Required] public string BaseUrl { get; set; } = "https://geocode-maps.yandex.ru/v1/";
    [Required] public string Lang { get; set; } = "ru_RU";
    [Range(1, 300)] public int TimeoutSeconds { get; set; } = 30;
}
