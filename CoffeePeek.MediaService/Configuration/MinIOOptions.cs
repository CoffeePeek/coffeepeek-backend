namespace CoffeePeek.MediaService.Configuration;

public class MinIOOptions
{
    public string UserBucketName { get; set; } = null!;
    public string ShopBucketName { get; set; } = null!;
    public string CheckInBucketName { get; set; } = "coffeepeek.checkins";
    public string Endpoint { get; set; } = null!;
    public string SecretKey { get; set; } = null!;
    public string AccessKey { get; set; } = null!;
}
