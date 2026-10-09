using System.Net;
using CoffeePeek.Contract.Dtos.AppDownloads;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;

namespace CoffeePeek.Shops.Application.Features.AppDownloads;

public static class PublishAndroidReleaseHandler
{
    public static async Task<Response<AndroidReleaseDto>> Handle(
        PublishAndroidReleaseCommand command,
        IAppDistributionRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var target = await repository.GetAndroidReleaseByIdAsync(command.Id, ct);
        if (target is null)
            return Response<AndroidReleaseDto>.Error(HttpStatusCode.NotFound, "Android release not found.");

        var published = await repository.GetPublishedAndroidReleasesAsync(ct);
        var previousActive = published.Where(r => r.Id != target.Id).ToArray();
        foreach (var release in previousActive)
            release.Unpublish();

        if (previousActive.Length > 0)
            await unitOfWork.SaveChangesAsync(ct);

        target.Publish();
        repository.AddAuditLog(AppDistributionAuditLog.Create(
            command.UserId,
            "AppDistribution.AndroidReleasePublished",
            target.Id,
            string.Join(',', previousActive.Select(r => r.Id)),
            $"activeReleaseId={target.Id};version={target.Version}"));

        return Response<AndroidReleaseDto>.Success(target.ToDto());
    }
}
