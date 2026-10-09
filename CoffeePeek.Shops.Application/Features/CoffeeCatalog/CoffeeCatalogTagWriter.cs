using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shops.Domain.Aggregates.RoasterTagAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed class CoffeeCatalogTagWriter(ICoffeeCatalogTagStorage storage) : ICoffeeCatalogTagWriter
{
    public async Task<RoasterTagDto> CreateTagAsync(CreateRoasterTagRequest request, CancellationToken ct)
    {
        if (await storage.IsSlugReservedAsync(request.Slug, ct)) throw new CoffeeCatalogException("Tag slug is reserved.", 409);
        var tag = new RoasterTag(request.Slug, request.Name, request.Description, request.SortOrder);
        await storage.SaveTagAsync(tag, true, ct); return Map(tag);
    }

    public async Task<RoasterTagDto?> EditTagAsync(Guid id, RoasterTagEdit request, CancellationToken ct)
    {
        var tag = await storage.FindAsync(id, ct);
        if (tag is null) return null;
        tag.Update(request.Name, request.Description, request.SortOrder, request.IsActive);
        await storage.SaveTagAsync(tag, false, ct); return Map(tag);
    }

    public async Task SetRoasterTagsAsync(Guid roasterId, Guid[] tagIds, Guid actorId, CancellationToken ct)
    {
        if (tagIds is null || tagIds.Length > 100) throw new CoffeeCatalogException("Invalid tag selection.");
        if (!await storage.RoasterExistsAsync(roasterId, ct)) throw new CoffeeCatalogException("Roaster not found.", 404);
        var ids = tagIds.Distinct().ToArray();
        if (await storage.CountActiveAsync(ids, ct) != ids.Length) throw new CoffeeCatalogException("Unknown or inactive roaster tag.");
        var existing = await storage.AssignmentsAsync(roasterId, ct);
        var removed = existing.Where(a => !ids.Contains(a.TagId)).ToArray();
        var added = ids.Except(existing.Select(a => a.TagId)).Select(id => new RoasterTagAssignment(roasterId, id, actorId)).ToArray();
        await storage.SaveAssignmentsAsync(added, removed, ct);
    }

    private static RoasterTagDto Map(RoasterTag tag) => new(tag.Id, tag.Slug, tag.Name, tag.Description, tag.SortOrder, tag.IsActive);
}
