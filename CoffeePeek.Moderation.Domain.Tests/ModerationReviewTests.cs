using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests;

public class ModerationReviewTests
{
    private static readonly Guid ValidShopId = Guid.NewGuid();
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly Guid ValidModerationShopId = Guid.NewGuid();
    private const string ValidUserName = "user";
    private const string ValidHeader = "Valid Header";
    private const string ValidComment = "Valid comment text here";
    private const int ValidRating = 3;

    [Fact]
    public void Create_WithNullModerationShopId_Succeeds()
    {
        var review = CreateReview(ValidShopId, moderationShopId: null);

        review.ShopId.Should().Be(ValidShopId);
        review.ModerationShopId.Should().BeNull();
        review.Header.Should().Be(ValidHeader);
        review.Comment.Should().Be(ValidComment);
    }

    [Fact]
    public void Create_WithEmptyModerationShopId_StoresNull()
    {
        var review = CreateReview(ValidShopId, Guid.Empty);

        review.ModerationShopId.Should().BeNull();
    }

    [Fact]
    public void Create_WithEmptyShopId_ThrowsDomainException()
    {
        var act = () => CreateReview(Guid.Empty, ValidModerationShopId);

        act.Should().Throw<DomainException>().WithMessage("*shopId*");
    }

    [Fact]
    public void Create_WithBlankHeader_StoresNullHeader()
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId, header: " ");

        review.Header.Should().BeNull();
    }

    [Fact]
    public void Create_WithHeaderTooShort_ThrowsDomainException()
    {
        var act = () => CreateReview(ValidShopId, ValidModerationShopId, header: "ab");

        act.Should().Throw<DomainException>().WithMessage("*header*");
    }

    [Fact]
    public void Create_WithBlankComment_ThrowsDomainException()
    {
        var act = () => CreateReview(ValidShopId, ValidModerationShopId, comment: "   ");

        act.Should().Throw<DomainException>().WithMessage("*comment*");
    }

    [Fact]
    public void Create_WithCommentTooShort_ThrowsDomainException()
    {
        var act = () => CreateReview(ValidShopId, ValidModerationShopId, comment: "Short");

        act.Should().Throw<DomainException>().WithMessage("*comment*");
    }

    [Fact]
    public void Approve_WhenPending_ReturnsTrueAndSetsApproved()
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId);
        var moderatorId = Guid.NewGuid();

        var changed = review.Approve(moderatorId);

        changed.Should().BeTrue();
        review.ModerationStatus.Should().Be(ModerationStatus.Approved);
        review.ModeratedBy.Should().Be(moderatorId);
    }

    [Fact]
    public void Approve_WhenAlreadyApproved_ReturnsFalseWithoutThrowing()
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId);
        var moderatorId = Guid.NewGuid();
        review.Approve(moderatorId);
        var moderatedAtAfterFirstApprove = review.ModeratedAt;

        var changed = false;
        var act = () => changed = review.Approve(moderatorId);

        act.Should().NotThrow();
        changed.Should().BeFalse();
        review.ModerationStatus.Should().Be(ModerationStatus.Approved);
        review.ModeratedAt.Should().Be(moderatedAtAfterFirstApprove);
    }

    [Fact]
    public void Reject_WithValidReasonAndModerator_SetsRejectedStatusReasonAndModerator()
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId);
        var moderatorId = Guid.NewGuid();

        review.Reject("Inappropriate content", moderatorId);

        review.ModerationStatus.Should().Be(ModerationStatus.Rejected);
        review.RejectedReason.Should().Be("Inappropriate content");
        review.ModeratedBy.Should().Be(moderatorId);
        review.ModeratedAt.Should().NotBeNull();
    }

    [Fact]
    public void Reject_WithEmptyModeratorId_ThrowsDomainException()
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId);

        var act = () => review.Reject("Inappropriate content", Guid.Empty);

        act.Should().Throw<DomainException>().WithMessage("*moderatorId*");
        review.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithNullOrWhitespaceReason_ThrowsDomainException(string? reason)
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId);
        var moderatorId = Guid.NewGuid();

        var act = () => review.Reject(reason!, moderatorId);

        act.Should().Throw<DomainException>().WithMessage("Reject reason is required.");
        review.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Fact]
    public void Reject_WithReasonExceedingMaxLength_ThrowsDomainException()
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId);
        var moderatorId = Guid.NewGuid();
        var tooLongReason = new string('a', BusinessConstants.MaxRejectReasonCommentLength + 1);

        var act = () => review.Reject(tooLongReason, moderatorId);

        act.Should().Throw<DomainException>()
            .WithMessage($"reason must be between {BusinessConstants.MinRejectReasonCommentLength} and {BusinessConstants.MaxRejectReasonCommentLength} characters.");
        review.ModerationStatus.Should().Be(ModerationStatus.Pending);
    }

    [Fact]
    public void MoveToPending_WithValidModerator_ResetsStatusAndClearsRejectedReason()
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId);
        var rejectingModeratorId = Guid.NewGuid();
        review.Reject("Inappropriate content", rejectingModeratorId);

        var movingModeratorId = Guid.NewGuid();
        review.MoveToPending(movingModeratorId);

        review.ModerationStatus.Should().Be(ModerationStatus.Pending);
        review.RejectedReason.Should().BeNull();
        review.ModeratedBy.Should().Be(movingModeratorId);
        review.ModeratedAt.Should().NotBeNull();
    }

    [Fact]
    public void MoveToPending_WithEmptyModeratorId_ThrowsDomainException()
    {
        var review = CreateReview(ValidShopId, ValidModerationShopId);

        var act = () => review.MoveToPending(Guid.Empty);

        act.Should().Throw<DomainException>().WithMessage("*moderatorId*");
    }

    private static ModerationReview CreateReview(
        Guid shopId,
        Guid? moderationShopId,
        string header = ValidHeader,
        string comment = ValidComment)
    {
        return ModerationReview.Create(
            ValidUserId,
            shopId,
            moderationShopId,
            ValidUserName,
            header,
            comment,
            ValidRating,
            ValidRating,
            ValidRating,
            new List<PhotoMetadata>());
    }
}
