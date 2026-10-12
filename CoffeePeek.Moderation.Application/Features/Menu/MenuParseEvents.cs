using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Domain.Aggregates.MenuDraftAggregate;

namespace CoffeePeek.Moderation.Application.Features.Menu;

public static class MenuParseEvents
{
    public static ParseMenuRequestedEvent? FromDraft(
        MenuParseSourceKind kind,
        Guid sourceId,
        Guid? publishedShopId,
        MenuDraft? draft,
        Guid? requestedByUserId)
    {
        if (draft is null || draft.Photos.Count == 0)
            return null;

        return new ParseMenuRequestedEvent(
            kind,
            sourceId,
            publishedShopId,
            draft.Photos.Select(p => new MenuPhotoRef(
                p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.MediaPhotoId)).ToArray(),
            requestedByUserId,
            draft.ParseRequestId);
    }
}
