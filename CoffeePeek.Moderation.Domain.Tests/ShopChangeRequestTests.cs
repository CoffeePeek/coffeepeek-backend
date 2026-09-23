using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests;

public class ShopChangeRequestTests
{
    [Fact]
    public void Create_CreatesPendingRequest()
    {
        var request = ShopChangeRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), ShopChangeSection.Description, "{\"description\":\"Better\"}");

        request.Status.Should().Be(ModerationStatus.Pending);
        request.ReviewedByUserId.Should().BeNull();
        request.ReviewedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Approve_IsIdempotent()
    {
        var moderatorId = Guid.NewGuid();
        var request = CreatePending();

        request.Approve(moderatorId, DateTime.UtcNow).Should().BeTrue();
        request.Approve(moderatorId, DateTime.UtcNow).Should().BeFalse();

        request.Status.Should().Be(ModerationStatus.Approved);
        request.ReviewedByUserId.Should().Be(moderatorId);
    }

    [Fact]
    public void Edit_AfterDecision_IsRejected()
    {
        var request = CreatePending();
        request.Approve(Guid.NewGuid(), DateTime.UtcNow);

        var act = () => request.Edit(ShopChangeSection.Contacts, "{}");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reject_RequiresReason()
    {
        var request = CreatePending();

        var act = () => request.Reject(Guid.NewGuid(), " ", DateTime.UtcNow);

        act.Should().Throw<DomainException>();
    }

    private static ShopChangeRequest CreatePending() => ShopChangeRequest.Create(
        Guid.NewGuid(), Guid.NewGuid(), ShopChangeSection.Description, "{\"description\":null}");

    [Fact]
    public void MarkApplyFailed_OnlyMovesApprovedRequestToRejected()
    {
        // Regression: a change request that Shops could not apply stayed Approved forever.
        var request = ShopChangeRequest.Create(Guid.NewGuid(), Guid.NewGuid(), ShopChangeSection.Description, "{}");
        request.MarkApplyFailed("shop deleted", DateTime.UnixEpoch).Should().BeFalse("still pending");

        request.Approve(Guid.NewGuid(), DateTime.UnixEpoch);
        request.MarkApplyFailed("shop deleted", DateTime.UnixEpoch).Should().BeTrue();

        request.Status.Should().Be(ModerationStatus.Rejected);
        request.RejectionReason.Should().Contain("shop deleted");
        request.MarkApplyFailed("again", DateTime.UnixEpoch).Should().BeFalse();
    }
}
