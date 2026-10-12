using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Shared.Kernel.Response;
using DomainSection = CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate.ShopChangeSection;
using DomainStatus = CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus;
using ShopChangeSection = CoffeePeek.Contract.Enums.ShopChangeSection;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public static class GetShopChangeRequestsHandler
{
    public static Task<Response<ShopChangeRequestPageDto>> Handle(
        GetShopChangeRequestsQuery query,
        IShopChangeRequestRepository repository,
        CancellationToken ct) => GetPage(
        query.Page, query.PageSize, query.Status, query.ShopId, query.SubmittedByUserId, query.Section, repository, ct);

    internal static async Task<Response<ShopChangeRequestPageDto>> GetPage(
        int page, int pageSize, ModerationStatus? status, Guid? shopId, Guid? userId,
        ShopChangeSection? section, IShopChangeRequestRepository repository, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await repository.GetPagedAsync(
            page, pageSize, status.HasValue ? (DomainStatus?)status.Value : null, shopId, userId,
            section.HasValue ? (DomainSection?)section.Value : null, ct);
        var result = new ShopChangeRequestPageDto(
            items.Select(ShopChangeRequestPayload.Map).ToList(), total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize), page, pageSize);
        return Response<ShopChangeRequestPageDto>.Success(result);
    }
}
