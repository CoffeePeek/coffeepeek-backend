namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public record UpdateAppVersionPolicyCommand(Guid UserId, string Platform, int LatestVersionCode, int MinVersionCode);
