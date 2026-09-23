using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using FluentAssertions;
using JetBrains.Annotations;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CheckInAggregate;

[TestSubject(typeof(CheckIn))]
public class CheckInTests
{
    [Fact]
    public void Create_WithEmptyUserId_ThrowsDomainException()
    {
        // Act
        Action act = () => CheckIn.Create(Guid.Empty, Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));

        // Assert
        act.Should().Throw<DomainException>().WithMessage("UserId is required.");
    }

    [Fact]
    public void Create_WithEmptyShopId_ThrowsDomainException()
    {
        // Act
        Action act = () => CheckIn.Create(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow.AddHours(-1));

        // Assert
        act.Should().Throw<DomainException>().WithMessage("ShopId is required.");
    }

    [Fact]
    public void Create_WithValidData_ReturnsCheckInWithAllFields()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var visitedAt = DateTime.UtcNow.AddHours(-1);

        // Act
        var checkIn = CheckIn.Create(userId, shopId, visitedAt);

        // Assert
        checkIn.Id.Should().NotBeEmpty();
        checkIn.UserId.Should().Be(userId);
        checkIn.ShopId.Should().Be(shopId);
        checkIn.VisitedAt.Should().Be(visitedAt);
    }

    [Fact]
    public void Create_WithValidData_HasDefaultZeroRating()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var visitedAt = DateTime.UtcNow.AddHours(-1);

        // Act
        var checkIn = CheckIn.Create(userId, shopId, visitedAt);

        // Assert
        checkIn.Rating.Should().NotBeNull();
        checkIn.Rating.Place.Should().Be(0);
        checkIn.Rating.Service.Should().Be(0);
        checkIn.Rating.Coffee.Should().Be(0);
    }

    [Fact]
    public void AssignRating_WithValidScores_SetsOwnedRating()
    {
        var checkIn = CheckIn.Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));

        checkIn.AssignRating(place: 5, service: 4, coffee: 3);

        checkIn.Rating.Place.Should().Be(5);
        checkIn.Rating.Service.Should().Be(4);
        checkIn.Rating.Coffee.Should().Be(3);
        checkIn.Rating.AverageRating.Should().Be(4m);
    }

    [Fact]
    public void UpdateNote_WithValue_SetsNote()
    {
        // Arrange
        var checkIn = CheckIn.Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));

        // Act
        checkIn.UpdateNote("  trim  ");

        // Assert
        checkIn.Note.Should().Be("trim");
    }

    [Fact]
    public void UpdateNote_WithNull_SetsNoteToNull()
    {
        // Arrange
        var checkIn = CheckIn.Create(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddHours(-1));

        // Act
        checkIn.UpdateNote(null);

        // Assert
        checkIn.Note.Should().BeNull();
    }
}
