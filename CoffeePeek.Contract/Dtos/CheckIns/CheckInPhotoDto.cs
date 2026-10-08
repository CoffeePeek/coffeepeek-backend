using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.CheckIns;

public record CheckInPhotoDto(Guid Id, string FileName, string ContentType, string StorageKey,
    long SizeBytes, int SortIndex, string Url);
