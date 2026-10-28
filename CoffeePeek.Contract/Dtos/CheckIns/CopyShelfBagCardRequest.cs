namespace CoffeePeek.Contract.Dtos.CheckIns;

/// <summary>
/// Shops asks Media to copy the confirmed card <paramref name="PhotoId"/> of bag <paramref name="BagId"/> into a new photo
/// <paramref name="CopyId"/> reserved for the check-in. Shops derives <paramref name="CopyId"/> deterministically, so a
/// retried request returns the same copy.
/// </summary>
public record CopyShelfBagCardRequest(Guid CheckInId, Guid BagId, Guid PhotoId, Guid CopyId);
