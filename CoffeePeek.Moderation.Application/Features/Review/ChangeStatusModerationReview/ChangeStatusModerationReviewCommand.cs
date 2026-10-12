using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain;

namespace CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;

public record ChangeStatusModerationReviewCommand(
    [property: JsonIgnore] Guid UserId,
    Guid SubmissionId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ModerationStatus ModerationStatus,
    [MaxLength(BusinessConstants.MaxRejectReasonCommentLength)] string? Comment,
    [MaxLength(BusinessConstants.MaxRejectReasonCommentLength)] string? RejectReason,
    [property: JsonIgnore] bool PendingOnly = false);
