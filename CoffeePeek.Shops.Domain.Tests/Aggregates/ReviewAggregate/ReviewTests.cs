using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using FluentAssertions;
using JetBrains.Annotations;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.ReviewAggregate;

[TestSubject(typeof(Review))]
public class ReviewTests
{
    private static readonly Guid ValidShopId = Guid.NewGuid();
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly Guid ValidModerationReviewId = Guid.NewGuid();
    private const string ValidHeader = "Valid Header";
    private const string ValidComment = "Valid comment text here";
    private const int ValidRating = 3;

    [Fact]
    public void Create_WithEmptyShopId_ThrowsDomainException()
    {
        // Act
        Action act = () => Review.Create(Guid.Empty, ValidUserId, "user", ValidHeader, ValidComment, ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithEmptyUserId_ThrowsDomainException()
    {
        // Act
        Action act = () => Review.Create(Guid.NewGuid(), Guid.Empty, "user", ValidHeader, ValidComment, ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithBlankHeader_StoresNullHeader()
    {
        // Act
        var review = Review.Create(ValidShopId, ValidUserId, "user", " ", ValidComment, ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        review.Header.Should().BeNull();
    }

    [Fact]
    public void Create_WithHeaderTooShort_ThrowsDomainException()
    {
        // Act — header "ab" is 2 chars, below MinReviewHeaderLength=3
        Action act = () => Review.Create(ValidShopId, ValidUserId, "user", "ab", ValidComment, ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithHeaderTooLong_ThrowsDomainException()
    {
        // Act — 101 chars, above MaxReviewHeaderLength=100
        Action act = () => Review.Create(ValidShopId, ValidUserId, "user", new string('a', 101), ValidComment, ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithBlankComment_ThrowsDomainException()
    {
        // Act
        Action act = () => Review.Create(ValidShopId, ValidUserId, "user", ValidHeader, "   ", ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithCommentTooShort_ThrowsDomainException()
    {
        // Act — "Short" is 5 chars, below MinReviewCommentLength=10
        Action act = () => Review.Create(ValidShopId, ValidUserId, "user", ValidHeader, "Short", ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithCommentTooLong_ThrowsDomainException()
    {
        // Act — 1001 chars, above MaxReviewCommentLength=1000
        Action act = () => Review.Create(ValidShopId, ValidUserId, "user", ValidHeader, new string('a', 1001), ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithValidData_ReturnsReviewWithAllFields()
    {
        // Act
        var review = Review.Create(ValidShopId, ValidUserId, "userName", ValidHeader, ValidComment, ValidRating, ValidRating, ValidRating, ValidModerationReviewId);

        // Assert
        review.CoffeeShopId.Should().Be(ValidShopId);
        review.UserId.Should().Be(ValidUserId);
        review.Header.Should().Be(ValidHeader);
        review.Comment.Should().NotBeNullOrEmpty();
        review.Rating.Should().NotBeNull();
        review.ModerationReviewId.Should().Be(ValidModerationReviewId);
    }
}
