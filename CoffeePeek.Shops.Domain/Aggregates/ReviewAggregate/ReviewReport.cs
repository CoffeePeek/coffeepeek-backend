using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public sealed class ReviewReport : Entity<Guid>
{
    public const int MaxTextLength = 2000;
    public Guid? CheckInId { get; private set; }
    public Guid ReportedByUserId { get; private set; }
    public string Text { get; private set; } = null!;
    public ReviewReportStatus Status { get; private set; }
    public Guid? ResolvedByAdminId { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public uint Version { get; private set; }

    private ReviewReport() { }

    public static ReviewReport Create(Guid checkInId, Guid userId, string text)
    {
        if (checkInId == Guid.Empty || userId == Guid.Empty)
            throw new ValidationException("Check-in and reporting user are required.");
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > MaxTextLength)
            throw new ValidationException($"Complaint text must contain 1–{MaxTextLength} characters.");

        return new ReviewReport
        {
            Id = Guid.NewGuid(), CheckInId = checkInId, ReportedByUserId = userId,
            Text = text.Trim(), CreatedAtUtc = DateTime.UtcNow
        };
    }

    public bool Resolve(bool deleteCheckIn, Guid adminId)
    {
        if (adminId == Guid.Empty)
            throw new ValidationException("Resolving admin is required.");
        var decision = deleteCheckIn ? ReviewReportStatus.CheckInDeleted : ReviewReportStatus.Dismissed;
        if (Status == decision) return false;
        if (Status != ReviewReportStatus.Pending)
            throw new ConflictException("This complaint has already been resolved with a different decision.");
        Status = decision;
        ResolvedByAdminId = adminId;
        ResolvedAtUtc = DateTime.UtcNow;
        return true;
    }
}
