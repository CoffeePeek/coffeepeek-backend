using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using Moq;
using Wolverine;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Tests.Features.Ownership;

public sealed class OwnershipClaimHandlerTests
{
    [Fact]
    public async Task SubmitResolvesPublicSlugAndDoesNotGrantAccess()
    {
        var f = new Fixture();
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(x => x.ResolveAsync(PublicAddressKind.CoffeeShop, "old-slug", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicAddressMetadata(f.Shop.Id, "business", "/coffee-shops/business", 1, true));
        var result = await OwnershipClaimHandler.Handle(new SubmitOwnershipClaimCommand(f.Applicant,
            BusinessType.CoffeeShop, "old-slug", " contact ", " evidence "), f.Store.Object,
            addresses.Object, Mock.Of<IUnitOfWork>(), CancellationToken.None);
        Assert.Equal(f.Shop.Id, result.Data.BusinessId);
        Assert.Equal("business", result.Data.BusinessSlugSnapshot);
        Assert.Equal(OwnershipClaimStatus.Pending, result.Data.Status);
        Assert.False(result.Data.HasCurrentAccess);
        Assert.Null(f.Shop.OwnerUserId);
        f.Store.Verify(x => x.AddClaim(It.Is<BusinessOwnershipClaim>(c => c.ApplicantUserId == f.Applicant)), Times.Once);
        f.Store.Verify(x => x.AddChange(It.IsAny<BusinessOwnershipChange>()), Times.Never);
    }

    [Fact]
    public async Task EvidenceMustAttachBeforeTheClaimCanBeSavedAndNeverGrantsAccess()
    {
        var f = new Fixture();
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(x => x.ResolveAsync(PublicAddressKind.CoffeeShop, "business", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicAddressMetadata(f.Shop.Id, "business", "/coffee-shops/business", 1));
        var evidence = new Mock<IOwnershipEvidencePhotos>();
        var photos = new[] { Guid.NewGuid() };
        var command = new SubmitOwnershipClaimCommand(f.Applicant, BusinessType.CoffeeShop,
            "business", "contact", "proof", photos);
        var result = await OwnershipClaimHandler.Handle(command, f.Store.Object, addresses.Object,
            Mock.Of<IUnitOfWork>(), CancellationToken.None, evidence.Object);
        Assert.Equal(photos, result.Data.EvidencePhotoIds);
        Assert.False(result.Data.HasCurrentAccess);
        evidence.Verify(x => x.AttachAsync(f.Applicant, result.Data.Id, photos, It.IsAny<CancellationToken>(), null), Times.Once);
        f.Store.Verify(x => x.AddClaim(It.IsAny<BusinessOwnershipClaim>()), Times.Once);
        f.Store.Invocations.Clear();
        evidence.Setup(x => x.AttachAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid[]>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ThrowsAsync(new ValidationException("Evidence was not found."));
        await Assert.ThrowsAsync<ValidationException>(() => OwnershipClaimHandler.Handle(command,
            f.Store.Object, addresses.Object, Mock.Of<IUnitOfWork>(), CancellationToken.None, evidence.Object));
        f.Store.Verify(x => x.AddClaim(It.IsAny<BusinessOwnershipClaim>()), Times.Never);
    }

    [Fact]
    public async Task RejectedEvidenceIsReusedForANewAttemptAndItsFailedSaveRetry()
    {
        var f = new Fixture();
        var photos = new[] { Guid.NewGuid() };
        var previous = BusinessOwnershipClaim.Create(BusinessType.CoffeeShop, f.Shop.Id, "Name", "business",
            f.Applicant, "contact", "proof", photos);
        previous.Review(false, f.Admin, "More proof required");
        f.Store.Setup(x => x.GetLatestClaimAsync(BusinessType.CoffeeShop, f.Shop.Id, f.Applicant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(previous);
        var addresses = Mock.Of<IPublicAddressService>(x => x.ResolveAsync(PublicAddressKind.CoffeeShop, "business",
            It.IsAny<CancellationToken>()) == Task.FromResult<PublicAddressMetadata?>(new(f.Shop.Id, "business", "/test", 1)));
        var evidence = new Mock<IOwnershipEvidencePhotos>();
        var command = new SubmitOwnershipClaimCommand(f.Applicant, BusinessType.CoffeeShop, "business", "contact", "proof", photos);
        var first = await OwnershipClaimHandler.Handle(command, f.Store.Object, addresses,
            Mock.Of<IUnitOfWork>(), CancellationToken.None, evidence.Object);
        var retry = await OwnershipClaimHandler.Handle(command, f.Store.Object, addresses,
            Mock.Of<IUnitOfWork>(), CancellationToken.None, evidence.Object);
        Assert.NotEqual(previous.Id, first.Data.Id);
        Assert.Equal(first.Data.Id, retry.Data.Id);
        Assert.Equal(OwnershipClaimStatus.Rejected, previous.Status);
        evidence.Verify(x => x.AttachAsync(f.Applicant, first.Data.Id, photos, It.IsAny<CancellationToken>(), previous.Id), Times.Exactly(2));
    }

    [Fact]
    public async Task DuplicatePendingAndOwnedBusinessesCannotBeClaimed()
    {
        var f = new Fixture();
        var addresses = new Mock<IPublicAddressService>();
        addresses.Setup(x => x.ResolveAsync(PublicAddressKind.CoffeeShop, "business", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicAddressMetadata(f.Shop.Id, "business", "/coffee-shops/business", 1));
        var command = new SubmitOwnershipClaimCommand(f.Applicant, BusinessType.CoffeeShop, "business", "contact", "evidence");
        f.Store.Setup(x => x.PendingExistsAsync(BusinessType.CoffeeShop, f.Shop.Id, f.Applicant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await Assert.ThrowsAsync<ConflictException>(() => OwnershipClaimHandler.Handle(command, f.Store.Object,
            addresses.Object, Mock.Of<IUnitOfWork>(), CancellationToken.None));
        f.Shop.AssignOwner(Guid.NewGuid());
        await Assert.ThrowsAsync<ConflictException>(() => OwnershipClaimHandler.Handle(command, f.Store.Object,
            addresses.Object, Mock.Of<IUnitOfWork>(), CancellationToken.None));
        f.Store.Verify(x => x.AddClaim(It.IsAny<BusinessOwnershipClaim>()), Times.Never);
    }

    [Fact]
    public async Task ApprovalGrantsAccessAndAuditOnce_RepeatAfterRevokeDoesNotRegrant()
    {
        var f = new Fixture();
        var command = new ReviewOwnershipClaimCommand(f.Claim.Id, f.Admin, true);
        var first = await f.Review(command);
        Assert.Equal(f.Applicant, f.Shop.OwnerUserId);
        Assert.Equal(1, f.Shop.ProfileRevision);
        Assert.True(first.Data.HasCurrentAccess);
        Assert.Equal(OwnershipClaimStatus.Approved, f.Claim.Status);
        f.Store.Verify(x => x.AddChange(It.Is<BusinessOwnershipChange>(c => c.ClaimId == f.Claim.Id
            && c.PreviousOwnerUserId == null && c.NewOwnerUserId == f.Applicant && c.ActorUserId == f.Admin)), Times.Once);
        await f.Ownership.AssignAsync(BusinessType.CoffeeShop, f.Shop.Id, null, f.Admin, "revoked", false, null, CancellationToken.None);
        var replay = await f.Review(command);
        Assert.Null(f.Shop.OwnerUserId);
        Assert.False(replay.Data.HasCurrentAccess);
        Assert.Equal(OwnershipClaimStatus.Approved, replay.Data.Status);
        f.Store.Verify(x => x.AddChange(It.IsAny<BusinessOwnershipChange>()), Times.Exactly(2));
    }

    [Fact]
    public async Task AnotherClaimCannotReplaceAnOwner()
    {
        var f = new Fixture();
        var existing = Guid.NewGuid();
        f.Shop.AssignOwner(existing);
        await Assert.ThrowsAsync<ConflictException>(() => f.Review(new(f.Claim.Id, f.Admin, true)));
        Assert.Equal(existing, f.Shop.OwnerUserId);
        Assert.Equal(OwnershipClaimStatus.Pending, f.Claim.Status);
        f.Store.Verify(x => x.AddChange(It.IsAny<BusinessOwnershipChange>()), Times.Never);
    }

    [Fact]
    public async Task ShopAssignmentUsesOneTrackedOwnerSnapshotEvenWhenProjectionSaysAlreadyAssigned()
    {
        var f = new Fixture();
        // A separate projection may disagree after a concurrent revoke; it must not turn the grant into a no-op.
        f.Store.Setup(x => x.GetTargetAsync(BusinessType.CoffeeShop, f.Shop.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessOwnershipTargetDto(BusinessType.CoffeeShop, f.Shop.Id,
                f.Shop.Name, "business", f.Applicant));

        var result = await f.Ownership.AssignAsync(BusinessType.CoffeeShop, f.Shop.Id,
            f.Applicant, f.Admin, null, false, null, CancellationToken.None);

        Assert.Equal(f.Applicant, result.OwnerUserId);
        Assert.Equal(f.Applicant, f.Shop.OwnerUserId);
        Assert.Equal(1, f.Shop.ProfileRevision);
        f.Store.Verify(x => x.GetTargetAsync(BusinessType.CoffeeShop, f.Shop.Id, It.IsAny<CancellationToken>()), Times.Never);
        f.Store.Verify(x => x.AddChange(It.Is<BusinessOwnershipChange>(c => c.PreviousOwnerUserId == null
            && c.NewOwnerUserId == f.Applicant)), Times.Once);
    }

    [Fact]
    public async Task FailedAccountLookupLeavesClaimPendingAndDoesNotGrant()
    {
        var f = new Fixture();
        f.Users.Setup(x => x.ExistsAsync(f.Applicant, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<ConflictException>(() => f.Review(new(f.Claim.Id, f.Admin, true)));
        Assert.Null(f.Shop.OwnerUserId);
        Assert.Equal(OwnershipClaimStatus.Pending, f.Claim.Status);
        f.Store.Verify(x => x.AddChange(It.IsAny<BusinessOwnershipChange>()), Times.Never);
    }

    [Fact]
    public async Task RejectionRequiresReasonAndNeverAssignsOwner()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => f.Review(new(f.Claim.Id, f.Admin, false)));
        var result = await f.Review(new(f.Claim.Id, f.Admin, false, "Cannot verify official contact"));
        Assert.Equal(OwnershipClaimStatus.Rejected, result.Data.Status);
        Assert.Null(f.Shop.OwnerUserId);
        f.Users.Verify(x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Store.Verify(x => x.AddChange(It.IsAny<BusinessOwnershipChange>()), Times.Never);
    }

    [Fact]
    public async Task ForeignClaimIsNotDisclosed()
    {
        var f = new Fixture();
        await Assert.ThrowsAsync<NotFoundException>(() => OwnershipClaimHandler.Handle(
            new GetOwnershipClaimByIdQuery(f.Claim.Id, Guid.NewGuid()), f.Store.Object, CancellationToken.None));
        var result = await OwnershipClaimHandler.Handle(new GetOwnershipClaimByIdQuery(f.Claim.Id, f.Applicant),
            f.Store.Object, CancellationToken.None);
        Assert.Null(result.Data.CurrentOwnerUserId);
    }

    [Fact]
    public async Task RoasterGrantUsesCatalogPortAndWritesSharedAudit()
    {
        var f = new Fixture();
        var roasterId = Guid.NewGuid();
        f.Store.Setup(x => x.GetTargetAsync(BusinessType.Roaster, roasterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessOwnershipTargetDto(BusinessType.Roaster, roasterId, "Roaster", "roaster", null));
        await f.Ownership.AssignAsync(BusinessType.Roaster, roasterId, f.Applicant, f.Admin, null, true,
            f.Claim.Id, CancellationToken.None);
        f.Roasters.Verify(x => x.AssignAsync(roasterId, f.Applicant, true, It.IsAny<CancellationToken>()), Times.Once);
        f.Store.Verify(x => x.AddChange(It.Is<BusinessOwnershipChange>(c => c.BusinessType == BusinessType.Roaster
            && c.BusinessId == roasterId && c.NewOwnerUserId == f.Applicant)), Times.Once);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task InvalidPagingIsRejected(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ValidationException>(() => OwnershipClaimHandler.Handle(
            new GetOwnershipClaimsQuery(Guid.NewGuid(), Page: page, PageSize: pageSize),
            Mock.Of<IBusinessOwnershipStore>(), CancellationToken.None));
    }

    private sealed class Fixture
    {
        public Guid Applicant { get; } = Guid.NewGuid();
        public Guid Admin { get; } = Guid.NewGuid();
        public DomainCoffeeShop Shop { get; } = new(Guid.NewGuid(), "Business", null, PriceRange.Cheap, Guid.NewGuid());
        public Mock<IBusinessOwnershipStore> Store { get; } = new();
        public Mock<IUserExistenceLookup> Users { get; } = new();
        public Mock<IRoasterOwnershipWriter> Roasters { get; } = new();
        public BusinessOwnershipClaim Claim { get; }
        public BusinessOwnershipService Ownership { get; }

        public Fixture()
        {
            Claim = BusinessOwnershipClaim.Create(BusinessType.CoffeeShop, Shop.Id, Shop.Name, "business", Applicant, "contact", "evidence");
            Store.Setup(x => x.GetClaimAsync(Claim.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Claim);
            Store.Setup(x => x.GetTargetAsync(BusinessType.CoffeeShop, Shop.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new BusinessOwnershipTargetDto(BusinessType.CoffeeShop, Shop.Id, Shop.Name, "business", Shop.OwnerUserId));
            Users.Setup(x => x.ExistsAsync(Applicant, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var shops = new Mock<ICoffeeShopRepository>();
            shops.Setup(x => x.GetByIdAsync(Shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Shop);
            Ownership = new BusinessOwnershipService(Store.Object, shops.Object, Roasters.Object, Users.Object, Mock.Of<IMessageBus>());
        }

        public Task<CoffeePeek.Shared.Kernel.Response.Response<OwnershipClaimDto>> Review(ReviewOwnershipClaimCommand command) =>
            OwnershipClaimHandler.Handle(command, Store.Object, Ownership, Mock.Of<IUnitOfWork>(), CancellationToken.None);
    }
}
