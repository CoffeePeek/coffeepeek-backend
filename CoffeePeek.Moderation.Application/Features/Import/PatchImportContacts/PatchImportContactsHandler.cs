using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Import.PatchImportContacts;

public static class PatchImportContactsHandler
{
    public static async Task<Response<ShopImportCandidateDto>> Handle(
        PatchImportContactsCommand command,
        IShopImportCandidateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var candidate = await repository.GetByIdAsync(command.Id, ct);
        if (candidate is null)
            return Response<ShopImportCandidateDto>.Error(
                System.Net.HttpStatusCode.NotFound,
                "Import candidate not found.");

        var patchInstagram = command.Instagram is not null;
        var patchPhone = command.Phone is not null;
        var patchWebsite = command.Website is not null;
        var patchHours = command.OpeningHours is not null;

        if (!patchInstagram && !patchPhone && !patchWebsite && !patchHours)
            return Response<ShopImportCandidateDto>.Success(ShopImportCandidateMapper.ToDto(candidate));

        try
        {
            candidate.PatchContacts(
                command.Instagram,
                patchInstagram,
                command.Phone,
                patchPhone,
                command.Website,
                patchWebsite,
                command.OpeningHours,
                patchHours);
        }
        catch (DomainException ex)
        {
            return Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.BadRequest, ex.Message);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShopImportCandidateDto>.Success(ShopImportCandidateMapper.ToDto(candidate));
    }
}
