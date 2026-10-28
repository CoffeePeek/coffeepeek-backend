using CoffeePeek.Contract.Constants;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

namespace CoffeePeek.Moderation.Application.Features.Import.DecideImportCandidate;

public static class ImportPublishFactory
{
    public static ImportCandidatePublishedItem FromCandidate(
        ShopImportCandidate candidate,
        Guid creatorId,
        bool overrideClosed)
    {
        return new ImportCandidatePublishedItem(
            candidate.Id,
            creatorId,
            candidate.Name!.Trim(),
            candidate.PublishAddress(),
            candidate.Latitude,
            candidate.Longitude,
            CitiesConsts.MinskId,
            candidate.Phone,
            candidate.Website,
            candidate.Instagram,
            (CoffeeShopType)(int)candidate.CoffeeFocus!.Value,
            candidate.TagSlugs.ToArray(),
            overrideClosed && candidate.GoogleBusinessStatus == ImportGoogleBusinessStatus.ClosedPermanently,
            candidate.ImportedFromFile,
            MenuDraftMapper.ToSnapshot(candidate.Menu),
            ImportOpeningHoursParser.Parse(candidate.OpeningHours));
    }
}
