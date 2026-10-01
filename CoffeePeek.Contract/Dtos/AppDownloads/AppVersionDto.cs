#nullable enable

namespace CoffeePeek.Contract.Dtos.AppDownloads;

/// <summary>Update policy for one platform. Clients with a build below <see cref="MinVersionCode"/> must update.</summary>
public record AppVersionDto(
    int VersionCode,
    int MinVersionCode,
    string Url);

public record AppVersionPolicyDto(
    int LatestVersionCode,
    int MinVersionCode);
