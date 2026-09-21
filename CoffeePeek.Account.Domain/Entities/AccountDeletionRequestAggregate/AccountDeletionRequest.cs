using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;

public class AccountDeletionRequest : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }
    public AccountDeletionRequestStatus Status { get; private set; }
    public AccountDeletionReason Reason { get; private set; }
    public string? OtherReason { get; private set; }
    public string ConfirmationTokenHash { get; private set; } = null!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime ResendAvailableAtUtc { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? LastResentAtUtc { get; private set; }
    public DateTime ResendWindowStartedAtUtc { get; private set; }
    public int EmailsSentInWindow { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public DeletionPartitionStatus AccountPartitionStatus { get; private set; }
    public DeletionPartitionStatus ShopsPartitionStatus { get; private set; }
    public DeletionPartitionStatus ModerationPartitionStatus { get; private set; }
    public DeletionPartitionStatus MediaPartitionStatus { get; private set; }
    public uint Version { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private AccountDeletionRequest() { }

    public static AccountDeletionRequest Create(
        Guid userId,
        string confirmationTokenHash,
        DateTime nowUtc,
        TimeSpan tokenLifetime,
        TimeSpan resendCooldown)
    {
        ValidateTokenHash(confirmationTokenHash);

        return new AccountDeletionRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = AccountDeletionRequestStatus.PendingConfirmation,
            ConfirmationTokenHash = confirmationTokenHash,
            RequestedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(tokenLifetime),
            ResendAvailableAtUtc = nowUtc.Add(resendCooldown),
            ResendWindowStartedAtUtc = nowUtc,
            EmailsSentInWindow = 1,
            AccountPartitionStatus = DeletionPartitionStatus.NotStarted,
            ShopsPartitionStatus = DeletionPartitionStatus.NotStarted,
            ModerationPartitionStatus = DeletionPartitionStatus.NotStarted,
            MediaPartitionStatus = DeletionPartitionStatus.NotStarted
        };
    }

    public bool CanResend(DateTime nowUtc, int maxEmailsPerHour)
    {
        if (Status != AccountDeletionRequestStatus.PendingConfirmation ||
            nowUtc < ResendAvailableAtUtc)
            return false;

        return nowUtc >= ResendWindowStartedAtUtc.AddHours(1) ||
               EmailsSentInWindow < maxEmailsPerHour;
    }

    public void Resend(
        string confirmationTokenHash,
        DateTime nowUtc,
        TimeSpan tokenLifetime,
        TimeSpan resendCooldown,
        int maxEmailsPerHour)
    {
        if (Status != AccountDeletionRequestStatus.PendingConfirmation)
            throw new DomainException("Only pending deletion requests can be resent.");
        if (nowUtc < ResendAvailableAtUtc)
            throw new DomainException("Deletion confirmation resend is not available yet.");
        if (nowUtc < ResendWindowStartedAtUtc.AddHours(1) &&
            EmailsSentInWindow >= maxEmailsPerHour)
            throw new DomainException("Hourly deletion confirmation email limit reached.");

        ValidateTokenHash(confirmationTokenHash);
        if (nowUtc >= ResendWindowStartedAtUtc.AddHours(1))
        {
            ResendWindowStartedAtUtc = nowUtc;
            EmailsSentInWindow = 0;
        }

        ConfirmationTokenHash = confirmationTokenHash;
        LastResentAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc.Add(tokenLifetime);
        ResendAvailableAtUtc = nowUtc.Add(resendCooldown);
        EmailsSentInWindow++;
    }

    public void Cancel(DateTime nowUtc)
    {
        if (Status != AccountDeletionRequestStatus.PendingConfirmation)
            throw new DomainException("Only pending deletion requests can be cancelled.");
        if (nowUtc > ExpiresAtUtc)
            throw new DomainException("Deletion confirmation token has expired.");

        Status = AccountDeletionRequestStatus.Cancelled;
        CancelledAtUtc = nowUtc;
    }

    public void Confirm(AccountDeletionReason reason, string? otherReason, DateTime nowUtc)
    {
        if (Status != AccountDeletionRequestStatus.PendingConfirmation)
            throw new DomainException("Only pending deletion requests can be confirmed.");
        if (nowUtc > ExpiresAtUtc)
            throw new DomainException("Deletion confirmation token has expired.");

        ValidateReason(reason, otherReason);
        Reason = reason;
        OtherReason = NormalizeOtherReason(reason, otherReason);
        Status = AccountDeletionRequestStatus.Processing;
        ConfirmedAtUtc = nowUtc;
    }

    public static void ValidateReason(AccountDeletionReason reason, string? otherReason)
    {
        if (!Enum.IsDefined(reason))
            throw new DomainException("Invalid account deletion reason.");
        if (reason == AccountDeletionReason.Other && string.IsNullOrWhiteSpace(otherReason))
            throw new DomainException("A reason is required when 'Other' is selected.");
        if (reason != AccountDeletionReason.Other && !string.IsNullOrWhiteSpace(otherReason))
            throw new DomainException("Other reason is only allowed when 'Other' is selected.");
        if (otherReason?.Trim().Length > 500)
            throw new DomainException("Account deletion reason cannot exceed 500 characters.");
    }

    private static void ValidateTokenHash(string confirmationTokenHash)
    {
        if (string.IsNullOrWhiteSpace(confirmationTokenHash))
            throw new DomainException("Confirmation token hash is required.");
    }

    private static string? NormalizeOtherReason(AccountDeletionReason reason, string? otherReason) =>
        reason == AccountDeletionReason.Other ? otherReason?.Trim() : null;
}
