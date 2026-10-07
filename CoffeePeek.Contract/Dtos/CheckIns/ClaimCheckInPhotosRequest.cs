namespace CoffeePeek.Contract.Dtos.CheckIns;

public record ClaimCheckInPhotosRequest(Guid CheckInId, string[] StorageKeys);
