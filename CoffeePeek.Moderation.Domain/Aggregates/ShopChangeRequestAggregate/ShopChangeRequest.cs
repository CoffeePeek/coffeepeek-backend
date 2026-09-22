using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;

public enum ShopChangeSection
{
    Photos = 0,
    Contacts = 1,
    Description = 2,
    Tags = 3,
    Roasters = 4,
    Equipment = 5,
    Menu = 6,
    BrewMethods = 7
}

public sealed class ShopChangeRequest : Entity<Guid>
{
    public Guid ShopId { get; private set; }
    public Guid SubmittedByUserId { get; private set; }
    public ShopChangeSection Section { get; private set; }
    public string PayloadJson { get; private set; } = null!;
    public ModerationStatus Status { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }

    private ShopChangeRequest() { }

    public static ShopChangeRequest Create(Guid shopId, Guid submittedByUserId, ShopChangeSection section, string payloadJson)
    {
        if (shopId == Guid.Empty) throw new DomainException("ShopId is required.");
        if (submittedByUserId == Guid.Empty) throw new DomainException("SubmittedByUserId is required.");
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new DomainException("Change payload is required.");

        return new ShopChangeRequest
        {
            Id = Guid.NewGuid(),
            ShopId = shopId,
            SubmittedByUserId = submittedByUserId,
            Section = section,
            PayloadJson = payloadJson,
            Status = ModerationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Edit(ShopChangeSection section, string payloadJson)
    {
        EnsurePending();
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new DomainException("Change payload is required.");
        Section = section;
        PayloadJson = payloadJson;
    }

    public bool Approve(Guid moderatorUserId, DateTime utcNow)
    {
        if (Status == ModerationStatus.Approved) return false;
        EnsurePending();
        EnsureModerator(moderatorUserId);
        Status = ModerationStatus.Approved;
        ReviewedByUserId = moderatorUserId;
        ReviewedAtUtc = utcNow;
        RejectionReason = null;
        return true;
    }

    public void Reject(Guid moderatorUserId, string reason, DateTime utcNow)
    {
        EnsurePending();
        EnsureModerator(moderatorUserId);
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Rejection reason is required.");
        var trimmed = reason.Trim();
        if (trimmed.Length > 500) throw new DomainException("Rejection reason cannot exceed 500 characters.");
        Status = ModerationStatus.Rejected;
        ReviewedByUserId = moderatorUserId;
        ReviewedAtUtc = utcNow;
        RejectionReason = trimmed;
    }

    private void EnsurePending()
    {
        if (Status != ModerationStatus.Pending)
            throw new DomainException("Only pending change requests can be modified or decided.");
    }

    private static void EnsureModerator(Guid moderatorUserId)
    {
        if (moderatorUserId == Guid.Empty) throw new DomainException("Moderator user ID is required.");
    }
}
