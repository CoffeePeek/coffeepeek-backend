using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Xunit;

namespace CoffeePeek.Account.Domain.Tests;

public class AccountDeletionRequestTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_ShouldInitializePendingRequestWithDefaultLifecycleState()
    {
        var request = AccountDeletionRequest.Create(
            Guid.NewGuid(),
            new string('a', 64),
            Now,
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(5));

        request.Status.Should().Be(AccountDeletionRequestStatus.PendingConfirmation);
        request.ExpiresAtUtc.Should().Be(Now.AddHours(24));
        request.ResendAvailableAtUtc.Should().Be(Now.AddMinutes(5));
        request.EmailsSentInWindow.Should().Be(1);
        request.AccountPartitionStatus.Should().Be(DeletionPartitionStatus.NotStarted);
        request.ShopsPartitionStatus.Should().Be(DeletionPartitionStatus.NotStarted);
        request.ModerationPartitionStatus.Should().Be(DeletionPartitionStatus.NotStarted);
        request.MediaPartitionStatus.Should().Be(DeletionPartitionStatus.NotStarted);
    }

    [Fact]
    public void Confirm_WithOtherReason_ShouldRequireAndNormalizeText()
    {
        var request = CreateRequest();
        var act = () => request.Confirm(AccountDeletionReason.Other, " ", Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Confirm_WithReasonOtherThanOther_ShouldRejectOtherReason()
    {
        var request = CreateRequest();
        var act = () => request.Confirm(AccountDeletionReason.PrivacyConcerns, "unexpected", Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Confirm_WithValidOtherReason_ShouldSetProcessingAndNormalizeReason()
    {
        var request = CreateRequest();

        request.Confirm(AccountDeletionReason.Other, "  custom reason  ", Now);

        request.Status.Should().Be(AccountDeletionRequestStatus.Processing);
        request.Reason.Should().Be(AccountDeletionReason.Other);
        request.OtherReason.Should().Be("custom reason");
        request.ConfirmedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Resend_WhenCooldownElapsed_ShouldRotateHashAndExpiry()
    {
        var request = CreateRequest();
        var resentAt = Now.AddMinutes(5);

        request.Resend(
            new string('b', 64),
            resentAt,
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(5),
            3);

        request.ConfirmationTokenHash.Should().Be(new string('b', 64));
        request.LastResentAtUtc.Should().Be(resentAt);
        request.ExpiresAtUtc.Should().Be(resentAt.AddHours(24));
    }

    [Fact]
    public void Resend_WhenHourlyLimitReached_ShouldRejectUntilWindowResets()
    {
        var request = CreateRequest();
        request.Resend(new string('b', 64), Now.AddMinutes(5), TimeSpan.FromHours(24), TimeSpan.FromMinutes(5), 2);

        request.CanResend(Now.AddMinutes(10), 2).Should().BeFalse();
        var act = () => request.Resend(
            new string('c', 64),
            Now.AddMinutes(10),
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(5),
            2);
        act.Should().Throw<DomainException>();

        request.CanResend(Now.AddHours(1), 2).Should().BeTrue();
    }

    [Fact]
    public void Cancel_ShouldOnlyAllowUnexpiredPendingRequest()
    {
        var request = CreateRequest();

        request.Cancel(Now.AddMinutes(1));

        request.Status.Should().Be(AccountDeletionRequestStatus.Cancelled);
        request.CancelledAtUtc.Should().Be(Now.AddMinutes(1));
        var act = () => request.Cancel(Now.AddMinutes(2));
        act.Should().Throw<DomainException>();
    }

    private static AccountDeletionRequest CreateRequest() =>
        AccountDeletionRequest.Create(
            Guid.NewGuid(),
            new string('a', 64),
            Now,
            TimeSpan.FromHours(24),
            TimeSpan.FromMinutes(5));
}
