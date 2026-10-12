using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Import.RefreshGoogleStatus;

public static class RefreshGoogleStatusHandler
{
    public static async Task<Response<ShopImportCandidateDto>> Handle(
        RefreshGoogleStatusCommand command,
        IShopImportCandidateRepository repository,
        IGooglePlacesLookup googlePlaces,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var candidate = await repository.GetByIdAsync(command.Id, ct);
        if (candidate is null)
            return Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.NotFound, "Import candidate not found.");

        if (!candidate.HasRealName)
            return Response<ShopImportCandidateDto>.Error(
                System.Net.HttpStatusCode.BadRequest,
                "Cannot look up Google status without a real name.");

        var now = DateTimeOffset.UtcNow;
        if (!command.Force && candidate.IsGoogleCacheFresh(now))
            return Response<ShopImportCandidateDto>.Success(ShopImportCandidateMapper.ToDto(candidate));

        try
        {
            var result = await googlePlaces.LookupAsync(candidate.Name!, candidate.Latitude, candidate.Longitude, ct);
            candidate.ApplyGoogleStatus(result.Status, result.MapsUri, result.FetchedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.ServiceUnavailable, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return Response<ShopImportCandidateDto>.Error(
                System.Net.HttpStatusCode.BadGateway,
                $"Google Places lookup failed: {ex.Message}");
        }

        return Response<ShopImportCandidateDto>.Success(ShopImportCandidateMapper.ToDto(candidate));
    }
}
