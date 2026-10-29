using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.OwnershipAggregate;

public sealed class BusinessOwnershipClaimTests
{
    [Fact]
    public void Create_TrimsEvidenceAndContactAndStartsPending()
    {
        var claim = Create(" contact ", " evidence ");
        Assert.Equal("contact", claim.Contact);
        Assert.Equal("evidence", claim.Evidence);
        Assert.Equal(OwnershipClaimStatus.Pending, claim.Status);
        Assert.Null(claim.ReviewedAtUtc);
        Assert.Equal(0, claim.Revision);
    }

    [Theory]
    [InlineData("", "evidence")]
    [InlineData("contact", " ")]
    public void EmptyContent_IsRejected(string contact, string evidence) =>
        Assert.Throws<ValidationException>(() => Create(contact, evidence));

    [Fact]
    public void OversizedContentAndInvalidIdentity_AreRejected()
    {
        Assert.Throws<ValidationException>(() => Create(new string('x', 321), "evidence"));
        Assert.Throws<ValidationException>(() => Create("contact", new string('x', 4001)));
        Assert.Throws<ValidationException>(() => BusinessOwnershipClaim.Create((BusinessType)99,
            Guid.NewGuid(), "Business", "business", Guid.NewGuid(), "contact", "evidence"));
        Assert.Throws<ValidationException>(() => BusinessOwnershipClaim.Create(BusinessType.CoffeeShop,
            Guid.Empty, "Business", "business", Guid.NewGuid(), "contact", "evidence"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DecisionIsIdempotentAndPreservesOriginalReviewer(bool approve)
    {
        var claim = Create();
        var actor = Guid.NewGuid();
        Assert.True(claim.Review(approve, actor, " original reason "));
        var reviewedAt = claim.ReviewedAtUtc;
        Assert.False(claim.Review(approve, Guid.NewGuid(), "different reason"));
        Assert.Equal(actor, claim.ReviewedByUserId);
        Assert.Equal(reviewedAt, claim.ReviewedAtUtc);
        Assert.Equal("original reason", claim.DecisionReason);
        Assert.Equal(1, claim.Revision);
        Assert.Throws<ConflictException>(() => claim.Review(!approve, actor, "other decision"));
    }

    [Fact]
    public void RejectRequiresReasonBeforeAnyMutation()
    {
        var claim = Create();
        Assert.Throws<ValidationException>(() => claim.Review(false, Guid.NewGuid(), " "));
        Assert.Equal(OwnershipClaimStatus.Pending, claim.Status);
        Assert.Equal(0, claim.Revision);
        Assert.Throws<ValidationException>(() => claim.Review(true, Guid.Empty, null));
        Assert.Throws<ValidationException>(() => claim.Review(true, Guid.NewGuid(), new string('x', 2001)));
    }

    [Fact]
    public void AuditRequiresARealChangeAndActor()
    {
        var owner = Guid.NewGuid();
        Assert.Throws<ValidationException>(() => BusinessOwnershipChange.Create(BusinessType.Roaster,
            Guid.NewGuid(), owner, owner, Guid.NewGuid(), null));
        Assert.Throws<ValidationException>(() => BusinessOwnershipChange.Create(BusinessType.Roaster,
            Guid.NewGuid(), null, owner, Guid.Empty, null));
        var change = BusinessOwnershipChange.Create(BusinessType.Roaster,
            Guid.NewGuid(), owner, null, Guid.NewGuid(), " revoke ");
        Assert.Equal("revoke", change.Reason);
        Assert.Null(change.NewOwnerUserId);
    }

    private static BusinessOwnershipClaim Create(string contact = "contact", string evidence = "evidence") =>
        BusinessOwnershipClaim.Create(BusinessType.CoffeeShop, Guid.NewGuid(), "Business", "business",
            Guid.NewGuid(), contact, evidence);
}
