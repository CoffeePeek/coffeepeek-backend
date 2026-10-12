using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel.Response;
using DomainStatus = CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate.ImportDuplicateStatus;

namespace CoffeePeek.Moderation.Application.Features.Import.GetImportDuplicates;

public static class GetImportDuplicatesHandler
{
    public static async Task<Response<GetImportDuplicatesResponse>> Handle(
        GetImportDuplicatesQuery query,
        IShopImportDuplicateSuggestionRepository suggestions,
        IShopImportCandidateRepository candidates,
        CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var status = query.Status is null ? (DomainStatus?)null : (DomainStatus)(int)query.Status.Value;

        var (items, total) = await suggestions.SearchAsync(status, page, pageSize, ct);
        var ids = items.SelectMany(s => new[] { s.LeftCandidateId, s.RightCandidateId }).Distinct().ToArray();
        var byId = await candidates.GetByIdsAsync(ids, ct);

        var dtos = new List<ImportDuplicateSuggestionDto>(items.Count);
        foreach (var item in items)
        {
            if (!byId.TryGetValue(item.LeftCandidateId, out var left)
                || !byId.TryGetValue(item.RightCandidateId, out var right))
                continue;

            dtos.Add(ShopImportCandidateMapper.ToDto(item, left, right));
        }

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return Response<GetImportDuplicatesResponse>.Success(
            new GetImportDuplicatesResponse(dtos, total, totalPages, page, pageSize));
    }
}
