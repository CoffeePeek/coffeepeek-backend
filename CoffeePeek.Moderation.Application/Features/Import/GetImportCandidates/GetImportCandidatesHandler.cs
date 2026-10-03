using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel.Response;
using ContractBucket = CoffeePeek.Contract.Enums.ImportCollectorBucket;
using ContractStatus = CoffeePeek.Contract.Enums.ImportQueueStatus;
using ContractRejectReason = CoffeePeek.Contract.Enums.ImportRejectReason;
using ContractSource = CoffeePeek.Contract.Enums.ImportSource;

namespace CoffeePeek.Moderation.Application.Features.Import.GetImportCandidates;

public record GetImportCandidatesQuery(
    ContractStatus? Status = ContractStatus.Pending,
    ContractBucket? Bucket = null,
    CoffeeShopType? Type = null,
    ContractRejectReason? RejectReason = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20,
    ContractSource? Source = null,
    string? RequiredSignal = null);

public record GetImportCandidatesResponse(
    IReadOnlyList<ShopImportCandidateDto> Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);

public static class GetImportCandidatesHandler
{
    public static async Task<Response<GetImportCandidatesResponse>> Handle(
        GetImportCandidatesQuery query,
        IShopImportCandidateRepository repository,
        CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var excludeStale = query.Status == ContractStatus.Pending && query.Bucket is null;

        var (items, total) = await repository.SearchAsync(
            query.Status is null ? null : ShopImportCandidateMapper.ToDomain(query.Status.Value),
            query.Bucket is null ? null : ShopImportCandidateMapper.ToDomain(query.Bucket.Value),
            query.Type is null ? null : ShopImportCandidateMapper.ToDomain(query.Type.Value),
            query.RejectReason is null ? null : ShopImportCandidateMapper.ToDomain(query.RejectReason.Value),
            query.Search,
            excludeStale,
            page,
            pageSize,
            ct,
            query.Source is null
                ? null
                : (Domain.Aggregates.ShopImportCandidateAggregate.ImportSource)(int)query.Source.Value,
            query.RequiredSignal);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        var dtos = items.Select(c => ShopImportCandidateMapper.ToDto(c)).ToList();

        return Response<GetImportCandidatesResponse>.Success(
            new GetImportCandidatesResponse(dtos, total, totalPages, page, pageSize));
    }
}
