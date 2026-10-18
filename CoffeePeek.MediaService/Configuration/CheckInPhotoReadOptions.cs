using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.MediaService.Configuration;

public sealed class CheckInPhotoReadOptions
{
    [Required] public string AccessKey { get; set; } = "";
    [Required] public string SecretKey { get; set; } = "";
}
