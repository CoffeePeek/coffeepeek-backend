using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.Moderation.Application.Features.Import.Menu;

public record AttachImportCandidateMenuPhotosCommand(
    Guid CandidateId,
    IReadOnlyList<UploadedPhotoDto> Photos,
    Guid UserId);
