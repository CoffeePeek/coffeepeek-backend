using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Application.Features.Import.Menu;

public record AttachImportCandidateMenuPhotosCommand(
    Guid CandidateId,
    IReadOnlyList<UploadedPhotoDto> Photos,
    Guid UserId);
