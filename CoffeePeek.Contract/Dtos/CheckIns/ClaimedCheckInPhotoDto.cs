namespace CoffeePeek.Contract.Dtos.CheckIns;

public record ClaimedCheckInPhotoDto(Guid PhotoId, string RequestedStorageKey, string StorageKey,
    string FileName, string ContentType, long SizeBytes);
