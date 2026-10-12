using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;

namespace CoffeePeek.Moderation.Domain.Tests;

public class ShopIssueReportTests
{
    private static readonly Guid ValidShopId = Guid.NewGuid();
    private static readonly Guid ValidUserId = Guid.NewGuid();

    [Fact]
    public void Create_UnknownCategory_IsRejected()
    {
        Action create = () => ShopIssueReport.Create(ValidUserId, ValidShopId, (ShopIssueCategory)99, "Description");
        create.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNonOtherCategoryAndNoDescription_Succeeds()
    {
        var report = CreateReport(ShopIssueCategory.ShopClosed, description: null);

        report.Category.Should().Be(ShopIssueCategory.ShopClosed);
        report.Description.Should().BeNull();
        report.Status.Should().Be(ShopIssueReportStatus.Submitted);
    }

    [Fact]
    public void Create_WithBlankDescription_StoresNull()
    {
        var report = CreateReport(ShopIssueCategory.IncorrectPhotos, description: "   ");

        report.Description.Should().BeNull();
    }

    [Fact]
    public void Create_WithOtherCategoryAndDescription_Succeeds()
    {
        var report = CreateReport(ShopIssueCategory.Other, description: "The whole place moved next door");

        report.Category.Should().Be(ShopIssueCategory.Other);
        report.Description.Should().Be("The whole place moved next door");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithOtherCategoryAndNoDescription_ThrowsDomainException(string? description)
    {
        var act = () => CreateReport(ShopIssueCategory.Other, description);

        act.Should().Throw<DomainException>().WithMessage("*Description is required*");
    }

    [Fact]
    public void Create_WithDescriptionExceedingMaxLength_ThrowsDomainException()
    {
        var tooLong = new string('a', BusinessConstants.MaxShopIssueReportDescriptionLength + 1);

        var act = () => CreateReport(ShopIssueCategory.OutdatedMenu, tooLong);

        act.Should().Throw<DomainException>().WithMessage("*description*");
    }

    [Fact]
    public void Create_WithEmptyShopId_ThrowsDomainException()
    {
        var act = () => ShopIssueReport.Create(ValidUserId, Guid.Empty, ShopIssueCategory.ShopClosed, null);

        act.Should().Throw<DomainException>().WithMessage("*shopId*");
    }

    [Fact]
    public void Create_WithEmptyUserId_ThrowsDomainException()
    {
        var act = () => ShopIssueReport.Create(Guid.Empty, ValidShopId, ShopIssueCategory.ShopClosed, null);

        act.Should().Throw<DomainException>().WithMessage("*reportedByUserId*");
    }

    [Fact]
    public void MarkReviewed_WithValidModerator_SetsStatusAndReviewer()
    {
        var report = CreateReport(ShopIssueCategory.WrongOpeningHours, null);
        var moderatorId = Guid.NewGuid();

        report.MarkReviewed(moderatorId);

        report.Status.Should().Be(ShopIssueReportStatus.Reviewed);
        report.ReviewedBy.Should().Be(moderatorId);
        report.ReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFixed_WithValidModerator_SetsStatusAndReviewer()
    {
        var report = CreateReport(ShopIssueCategory.IncorrectAddress, null);
        var moderatorId = Guid.NewGuid();

        report.MarkFixed(moderatorId);

        report.Status.Should().Be(ShopIssueReportStatus.Fixed);
        report.ReviewedBy.Should().Be(moderatorId);
    }

    [Fact]
    public void MarkInvalid_WithValidModerator_SetsStatusAndReviewer()
    {
        var report = CreateReport(ShopIssueCategory.IncorrectAddress, null);
        var moderatorId = Guid.NewGuid();

        report.MarkInvalid(moderatorId);

        report.Status.Should().Be(ShopIssueReportStatus.Invalid);
        report.ReviewedBy.Should().Be(moderatorId);
    }

    [Fact]
    public void MarkReviewed_WithEmptyModeratorId_ThrowsDomainException()
    {
        var report = CreateReport(ShopIssueCategory.IncorrectAddress, null);

        var act = () => report.MarkReviewed(Guid.Empty);

        act.Should().Throw<DomainException>().WithMessage("*moderatorId*");
        report.Status.Should().Be(ShopIssueReportStatus.Submitted);
    }

    private static ShopIssueReport CreateReport(ShopIssueCategory category, string? description)
    {
        return ShopIssueReport.Create(ValidUserId, ValidShopId, category, description);
    }

    [Fact]
    public void Transition_FromClosedStatus_Throws()
    {
        var report = ShopIssueReport.Create(ValidUserId, ValidShopId, ShopIssueCategory.ShopClosed, null);
        report.MarkFixed(Guid.NewGuid());

        var act = () => report.MarkReviewed(Guid.NewGuid());

        act.Should().Throw<DomainException>();
        report.Status.Should().Be(ShopIssueReportStatus.Fixed);
    }
}
