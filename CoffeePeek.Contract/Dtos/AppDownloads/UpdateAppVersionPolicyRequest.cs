#nullable enable

namespace CoffeePeek.Contract.Dtos.AppDownloads;

public record UpdateAppVersionPolicyRequest(int LatestVersionCode, int MinVersionCode);
