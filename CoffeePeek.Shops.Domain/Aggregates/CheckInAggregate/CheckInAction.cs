using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

public partial class CheckIn
{
    public void AddPhotos(IEnumerable<ShopPhoto> photos)
    {
        EnsureNotDeleted();
        _shopPhotos.AddRange(photos);
        Touch();
    }

    public static CheckIn CreateUnified(Guid userId, Guid shopId, string username, string text,
        Rating rating, DateTime visitedAt, CheckInVisibility visibility = CheckInVisibility.Private,
        Guid? id = null)
    {
        ValidateContent(text, rating);
        ValidateVisibility(visibility);
        if (visitedAt == default || visitedAt.Kind != DateTimeKind.Utc
            || visitedAt > DateTime.UtcNow.AddMinutes(BusinessConstants.MaxVisitedAtClockSkewMinutes))
            throw new DomainException("VisitedAt must be a valid UTC visit time and cannot be in the future.");
        if (userId == Guid.Empty) throw new DomainException("UserId is required.");
        if (shopId == Guid.Empty) throw new DomainException("ShopId is required.");
        if (id == Guid.Empty) throw new DomainException("CheckInId is required.");
        var checkIn = new CheckIn(userId, shopId, visitedAt, text, rating, id);
        checkIn.Username = username;
        checkIn.Note = text.Trim();
        checkIn.Rating = rating;
        checkIn.Visibility = visibility;
        checkIn.ResetModeration();
        return checkIn;
    }

    public void UpdateContent(string text, Rating rating)
    {
        EnsureNotDeleted();
        ValidateContent(text, rating);
        Note = text.Trim();
        Rating = rating;
        ContentRevision++;
        ResetModeration();
        Touch();
    }

    public void ChangeVisibility(CheckInVisibility visibility)
    {
        EnsureNotDeleted();
        ValidateVisibility(visibility);
        if (Visibility == visibility) return;
        if (visibility == CheckInVisibility.Public) ValidateContent(Note!, Rating);
        Visibility = visibility;
        ContentRevision++;
        ResetModeration();
        Touch();
    }

    public void Delete()
    {
        if (DeletedAtUtc != null) return;
        DeletedAtUtc = DateTime.UtcNow;
        ContentRevision++;
        ResetModeration();
        Touch();
    }

    public bool ApplyModerationDecision(int revision, Guid submissionId, long sequence,
        CheckInModerationState state, string? reason, DateTime decidedAt)
    {
        if (DeletedAtUtc != null || Visibility != CheckInVisibility.Public
            || revision != ContentRevision || submissionId == Guid.Empty || sequence <= ModerationDecisionSequence
            || (ModerationSubmissionId != null && ModerationSubmissionId != submissionId)
            || state is not (CheckInModerationState.Pending or CheckInModerationState.Approved or CheckInModerationState.Rejected))
            return false;
        ModerationSubmissionId = submissionId;
        ModerationDecisionSequence = sequence;
        ModerationState = state;
        ApprovedRevision = state == CheckInModerationState.Approved ? ContentRevision : null;
        RejectionReason = state == CheckInModerationState.Rejected ? reason : null;
        if (state == CheckInModerationState.Approved) FirstPublishedAtUtc ??= decidedAt;
        Touch();
        return true;
    }

    public void SetHelpfulCount(int count)
    {
        if (count < 0) throw new DomainException("HelpfulCount cannot be negative.");
        HelpfulCount = count;
        Touch();
    }

    private static void ValidateContent(string text, Rating rating)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > BusinessConstants.MaxCheckInNoteLength)
            throw new DomainException($"Text is required and must not exceed {BusinessConstants.MaxCheckInNoteLength} characters.");
        if (rating == null) throw new DomainException("Rating is required.");
        Rating.Create(rating.Place, rating.Service, rating.Coffee);
    }

    private static void ValidateVisibility(CheckInVisibility visibility)
    {
        if (!Enum.IsDefined(visibility)) throw new DomainException("Visibility must be Private or Public.");
    }

    private void ResetModeration()
    {
        ModerationState = Visibility == CheckInVisibility.Public && DeletedAtUtc == null
            ? CheckInModerationState.Pending : CheckInModerationState.NotSubmitted;
        ApprovedRevision = null;
        ModerationSubmissionId = null;
        ModerationDecisionSequence = 0;
        RejectionReason = null;
    }

    private void EnsureNotDeleted()
    {
        if (DeletedAtUtc != null) throw new DomainException("Deleted check-ins cannot be changed.");
    }

    private void Touch() { RowVersion = Guid.NewGuid(); UpdatedAtUtc = DateTime.UtcNow; }
}
