using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Admin.Audit;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using DomainSection = CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate.ShopChangeSection;
using DomainStatus = CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus;
using ShopChangeSection = CoffeePeek.Contract.Enums.ShopChangeSection;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public record CreateShopChangeRequestCommand(
    Guid ShopId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ShopChangeSection Section,
    ShopChangePayloadDto Payload)
{
    [JsonIgnore] public Guid UserId { get; init; }
}

public record UpdateShopChangeRequestCommand(
    Guid RequestId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ShopChangeSection Section,
    ShopChangePayloadDto Payload,
    [property: JsonIgnore] Guid ModeratorUserId);

public record DecideShopChangeRequestCommand(
    Guid RequestId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationStatus Status,
    string? Comment,
    [property: JsonIgnore] Guid ModeratorUserId);

public record GetShopChangeRequestQuery(Guid RequestId, Guid UserId, bool IsModerator);

public record GetShopChangeRequestsQuery(
    int Page = 1,
    int PageSize = 20,
    ModerationStatus? Status = null,
    Guid? ShopId = null,
    ShopChangeSection? Section = null,
    Guid? SubmittedByUserId = null);

public record GetMyShopChangeRequestsQuery(
    Guid UserId,
    int Page = 1,
    int PageSize = 20,
    ModerationStatus? Status = null,
    Guid? ShopId = null,
    ShopChangeSection? Section = null);

public static class ShopChangeRequestPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(ShopChangeSection section, ShopChangePayloadDto payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Validate(section, payload);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static ShopChangePayloadDto Deserialize(string json) =>
        JsonSerializer.Deserialize<ShopChangePayloadDto>(json, JsonOptions)
        ?? throw new JsonException("Stored shop change payload is invalid.");

    public static ShopChangeRequestDto Map(ShopChangeRequest request) => new(
        request.Id,
        request.ShopId,
        request.SubmittedByUserId,
        (ShopChangeSection)request.Section,
        Deserialize(request.PayloadJson),
        (ModerationStatus)request.Status,
        request.ReviewedByUserId,
        request.ReviewedAtUtc,
        request.RejectionReason,
        request.CreatedAtUtc,
        request.UpdatedAtUtc);

    private static void Validate(ShopChangeSection section, ShopChangePayloadDto payload)
    {
        if (section != ShopChangeSection.Description && payload.Description is not null)
            throw new ArgumentException("Payload contains unrelated section fields.");

        var otherCount = (payload.Contacts is not null ? 1 : 0)
                         + (payload.Photos is not null ? 1 : 0)
                         + (payload.TagIds is not null ? 1 : 0)
                         + (payload.RoasterIds is not null ? 1 : 0)
                         + (payload.EquipmentIds is not null ? 1 : 0)
                         + (payload.Menu is not null ? 1 : 0)
                         + (payload.BrewMethodIds is not null ? 1 : 0);

        switch (section)
        {
            case ShopChangeSection.Description:
                if (otherCount != 0) throw new ArgumentException("Description request contains unrelated fields.");
                if (payload.Description?.Length > 1000) throw new ArgumentException("Description cannot exceed 1000 characters.");
                break;
            case ShopChangeSection.Contacts:
                RequireOnly(payload.Contacts, otherCount, 1, "Contacts");
                ValidateContacts(payload.Contacts!);
                break;
            case ShopChangeSection.Photos:
                RequireOnly(payload.Photos, otherCount, 1, "Photos");
                ValidateGallery(payload.Photos!);
                break;
            case ShopChangeSection.Tags:
                RequireIds(payload.TagIds, otherCount, "TagIds");
                break;
            case ShopChangeSection.Roasters:
                RequireIds(payload.RoasterIds, otherCount, "RoasterIds");
                break;
            case ShopChangeSection.Equipment:
                RequireIds(payload.EquipmentIds, otherCount, "EquipmentIds");
                break;
            case ShopChangeSection.BrewMethods:
                RequireIds(payload.BrewMethodIds, otherCount, "BrewMethodIds");
                break;
            case ShopChangeSection.Menu:
                RequireOnly(payload.Menu, otherCount, 1, "Menu");
                ValidateMenu(payload.Menu!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(section));
        }
    }

    private static void RequireOnly(object? value, int populatedCount, int expectedCount, string field)
    {
        if (value is null) throw new ArgumentException($"{field} is required for this section.");
        if (populatedCount != expectedCount) throw new ArgumentException("Payload contains unrelated section fields.");
    }

    private static void RequireIds(IReadOnlyList<Guid>? ids, int populatedCount, string field)
    {
        RequireOnly(ids, populatedCount, 1, field);
        var values = ids!;
        if (values.Any(x => x == Guid.Empty)) throw new ArgumentException($"{field} cannot contain empty IDs.");
        if (values.Distinct().Count() != values.Count) throw new ArgumentException($"{field} cannot contain duplicates.");
    }

    private static void ValidateContacts(ShopChangeContactsDto contacts)
    {
        if (contacts.PhoneNumber?.Length > CoffeePeek.Moderation.Domain.BusinessConstants.MaxShopContactPhoneNumberLength)
            throw new ArgumentException("Phone number is too long.");
        if (contacts.Email?.Length > CoffeePeek.Moderation.Domain.BusinessConstants.MaxShopContactEmailLength)
            throw new ArgumentException("Email is too long.");
        if (contacts.SiteLink?.Length > CoffeePeek.Moderation.Domain.BusinessConstants.MaxShopContactSiteLinkLength)
            throw new ArgumentException("Site link is too long.");
        if (contacts.InstagramLink?.Length > CoffeePeek.Moderation.Domain.BusinessConstants.MaxShopContactInstagramLinkLength)
            throw new ArgumentException("Instagram link is too long.");
    }

    private static void ValidateGallery(ShopChangeGalleryDto gallery)
    {
        ArgumentNullException.ThrowIfNull(gallery.RetainedPhotoIds);
        ArgumentNullException.ThrowIfNull(gallery.NewPhotos);
        if (gallery.RetainedPhotoIds.Distinct().Count() != gallery.RetainedPhotoIds.Count)
            throw new ArgumentException("Retained photo IDs cannot contain duplicates.");
        ValidatePhotos(gallery.NewPhotos);
    }

    private static void ValidateMenu(ShopChangeMenuDto menu)
    {
        if (menu.ExpectedInputRevision is null or < 0)
            throw new ArgumentException("Expected menu revision is required and cannot be negative.");
        ArgumentNullException.ThrowIfNull(menu.Items);
        ArgumentNullException.ThrowIfNull(menu.RetainedPhotoIds);
        ArgumentNullException.ThrowIfNull(menu.NewPhotos);
        if (menu.Items.Select(x => x.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() != menu.Items.Count)
            throw new ArgumentException("Menu item slugs cannot contain duplicates.");
        if (menu.RetainedPhotoIds.Distinct().Count() != menu.RetainedPhotoIds.Count)
            throw new ArgumentException("Retained menu photo IDs cannot contain duplicates.");
        ValidatePhotos(menu.NewPhotos);
    }

    private static void ValidatePhotos(IReadOnlyList<CoffeePeek.Contract.Dtos.UploadedPhotoDto> photos)
    {
        if (photos.Select(x => x.StorageKey).Distinct(StringComparer.Ordinal).Count() != photos.Count)
            throw new ArgumentException("New photo storage keys cannot contain duplicates.");
        if (photos.Any(x => string.IsNullOrWhiteSpace(x.FileName)
                            || string.IsNullOrWhiteSpace(x.ContentType)
                            || string.IsNullOrWhiteSpace(x.StorageKey)
                            || x.Size <= 0))
            throw new ArgumentException("Every new photo must contain valid upload metadata.");
    }
}

public static class CreateShopChangeRequestHandler
{
    public static async Task<Response<ShopChangeRequestDto>> Handle(
        CreateShopChangeRequestCommand command,
        IShopChangeRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        string json;
        try { json = ShopChangeRequestPayload.Serialize(command.Section, command.Payload); }
        catch (ArgumentException ex) { return Response<ShopChangeRequestDto>.Error(HttpStatusCode.BadRequest, ex.Message); }

        var request = ShopChangeRequest.Create(command.ShopId, command.UserId, (DomainSection)command.Section, json);
        repository.Add(request);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShopChangeRequestDto>.Success(ShopChangeRequestPayload.Map(request));
    }
}

public static class UpdateShopChangeRequestHandler
{
    public static async Task<Response<ShopChangeRequestDto>> Handle(
        UpdateShopChangeRequestCommand command,
        IShopChangeRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var request = await repository.GetByIdAsync(command.RequestId, ct);
        if (request is null) return Response<ShopChangeRequestDto>.Error(HttpStatusCode.NotFound, "Change request not found.");

        string json;
        try { json = ShopChangeRequestPayload.Serialize(command.Section, command.Payload); }
        catch (ArgumentException ex) { return Response<ShopChangeRequestDto>.Error(HttpStatusCode.BadRequest, ex.Message); }

        request.Edit((DomainSection)command.Section, json);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShopChangeRequestDto>.Success(ShopChangeRequestPayload.Map(request));
    }
}

public static class GetShopChangeRequestHandler
{
    public static async Task<Response<ShopChangeRequestDto>> Handle(
        GetShopChangeRequestQuery query,
        IShopChangeRequestRepository repository,
        CancellationToken ct)
    {
        var request = query.IsModerator
            ? await repository.GetByIdAsync(query.RequestId, ct)
            : await repository.GetByIdForUserAsync(query.RequestId, query.UserId, ct);
        return request is null
            ? Response<ShopChangeRequestDto>.Error(HttpStatusCode.NotFound, "Change request not found.")
            : Response<ShopChangeRequestDto>.Success(ShopChangeRequestPayload.Map(request));
    }
}

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

public static class GetMyShopChangeRequestsHandler
{
    public static Task<Response<ShopChangeRequestPageDto>> Handle(
        GetMyShopChangeRequestsQuery query,
        IShopChangeRequestRepository repository,
        CancellationToken ct) => GetShopChangeRequestsHandler.GetPage(
        query.Page, query.PageSize, query.Status, query.ShopId, query.UserId, query.Section, repository, ct);
}

public static class DecideShopChangeRequestHandler
{
    public static async Task<(Response, object?)> Handle(
        DecideShopChangeRequestCommand command,
        IShopChangeRequestRepository repository,
        IModerationAuditLogRepository auditRepository,
        CancellationToken ct)
    {
        var request = await repository.GetByIdAsync(command.RequestId, ct);
        if (request is null) return (Response.Error(404, "Change request not found."), null);

        object? outbound = null;
        if (command.Status == ModerationStatus.Approved)
        {
            var now = DateTime.UtcNow;
            if (request.Approve(command.ModeratorUserId, now))
            {
                outbound = new ShopChangeRequestApprovedV1(
                    request.Id, request.ShopId, request.SubmittedByUserId,
                    (ShopChangeSection)request.Section,
                    ShopChangeRequestPayload.Deserialize(request.PayloadJson),
                    command.ModeratorUserId, now);
                await ModerationAuditWriter.WriteAsync(
                    auditRepository, ModerationAuditEntityType.ShopChangeRequest, request.Id,
                    $"{request.ShopId}:{request.Section}", ModerationAuditAction.Approved,
                    command.ModeratorUserId, null, ct);
            }
        }
        else if (command.Status == ModerationStatus.Rejected)
        {
            request.Reject(command.ModeratorUserId, command.Comment ?? string.Empty, DateTime.UtcNow);
            await ModerationAuditWriter.WriteAsync(
                auditRepository, ModerationAuditEntityType.ShopChangeRequest, request.Id,
                $"{request.ShopId}:{request.Section}", ModerationAuditAction.Rejected,
                command.ModeratorUserId, command.Comment, ct);
        }
        else
        {
            return (Response.Error(400, "Status can only be Approved or Rejected."), null);
        }

        return (Response.Success(), outbound);
    }
}
