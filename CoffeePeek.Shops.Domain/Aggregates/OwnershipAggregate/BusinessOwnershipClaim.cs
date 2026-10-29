using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

public sealed class BusinessOwnershipClaim : Entity<Guid>
{
    public const int MaxContactLength = 320;
    public const int MaxEvidenceLength = 4000;
    public const int MaxReasonLength = 2000;
    public BusinessType BusinessType { get; private set; }
    public Guid BusinessId { get; private set; }
    public string BusinessNameSnapshot { get; private set; } = null!;
    public string BusinessSlugSnapshot { get; private set; } = null!;
    public Guid ApplicantUserId { get; private set; }
    public string Contact { get; private set; } = null!;
    public Guid[] EvidencePhotoIds { get; private set; } = [];
    public string Evidence { get; private set; } = null!;
    public OwnershipClaimStatus Status { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? DecisionReason { get; private set; }
    public long Revision { get; private set; }

    private BusinessOwnershipClaim() { }

    public static BusinessOwnershipClaim Create(BusinessType businessType, Guid businessId,
        string name, string slug, Guid applicantUserId, string contact, string evidence, Guid[]? evidencePhotoIds = null)
    {
        if (!Enum.IsDefined(businessType) || businessId == Guid.Empty || applicantUserId == Guid.Empty)
            throw new ValidationException("A valid business and applicant are required.");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new ValidationException("Business name and public slug are required.");
        if (string.IsNullOrWhiteSpace(contact) || contact.Trim().Length > MaxContactLength)
            throw new ValidationException($"Contact must contain 1–{MaxContactLength} characters.");
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Trim().Length > MaxEvidenceLength)
            throw new ValidationException($"Evidence must contain 1–{MaxEvidenceLength} characters.");
        var photos = ValidateEvidence(evidencePhotoIds ?? []);
        return new BusinessOwnershipClaim
        {
            Id = EvidenceIdentity(applicantUserId, businessType, businessId, photos), EvidencePhotoIds = photos, BusinessType = businessType, BusinessId = businessId,
            BusinessNameSnapshot = name, BusinessSlugSnapshot = slug, ApplicantUserId = applicantUserId,
            Contact = contact.Trim(), Evidence = evidence.Trim(), CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static Guid[] ValidateEvidence(Guid[] photoIds)
    {
        if (photoIds.Length > 3 || photoIds.Any(id => id == Guid.Empty) || photoIds.Distinct().Count() != photoIds.Length)
            throw new ValidationException("Up to three distinct evidence photos are supported.");
        return photoIds.ToArray();
    }

    private static Guid EvidenceIdentity(Guid applicant, BusinessType type, Guid business, Guid[] photos)
    {
        if (photos.Length == 0) return Guid.NewGuid();
        var identity = $"{applicant:N}:{type}:{business:N}:" +
            string.Join(",", photos.Order().Select(id => id.ToString("N")));
        return new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
    }

    public bool Review(bool approve, Guid actorUserId, string? reason)
    {
        if (!ValidateReview(approve, actorUserId, reason)) return false;
        Status = approve ? OwnershipClaimStatus.Approved : OwnershipClaimStatus.Rejected;
        ReviewedByUserId = actorUserId;
        ReviewedAtUtc = DateTime.UtcNow;
        DecisionReason = NormalizeReason(reason, required: !approve);
        Revision = checked(Revision + 1);
        return true;
    }

    public bool ValidateReview(bool approve, Guid actorUserId, string? reason)
    {
        if (actorUserId == Guid.Empty) throw new ValidationException("Reviewing administrator is required.");
        var decision = approve ? OwnershipClaimStatus.Approved : OwnershipClaimStatus.Rejected;
        if (Status == decision) return false;
        if (Status != OwnershipClaimStatus.Pending)
            throw new ConflictException("This ownership claim already has a different decision.");
        NormalizeReason(reason, required: !approve);
        return true;
    }

    public static string? NormalizeReason(string? reason, bool required)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if ((required && reason is null) || reason?.Length > MaxReasonLength)
            throw new ValidationException($"A reason of at most {MaxReasonLength} characters is required.");
        return reason;
    }
}
