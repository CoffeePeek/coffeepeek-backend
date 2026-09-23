namespace CoffeePeek.Contract.Dtos.Menu;

public record AttachMenuPhotosRequest(IReadOnlyList<CoffeePeek.Contract.Dtos.UploadedPhotoDto> Photos);

public record RemoveMenuPhotosRequest(IReadOnlyList<Guid> PhotoIds);
