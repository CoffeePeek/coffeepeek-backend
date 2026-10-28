using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class RoasterOwnershipRevisionLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public RoasterOwnershipRevisionLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminAndOwnerLoadedSameRevision_OnlyFirstProfileCommits(bool adminFirst)
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var roaster = await SeedAsync(owner, ct);
        await using var adminDb = fixture.Context();
        await using var ownerDb = fixture.Context();
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<RoasterDto>(It.IsAny<Roaster>())).Returns(() => new RoasterDto());

        var adminResponse = await UpdateRoasterHandler.Handle(new UpdateRoasterCommand(roaster.Id, "Admin edit")
            { ExpectedRevision = 1 }, new RoasterRepository(adminDb), mapper.Object,
            Mock.Of<IUploadedPhotoLookup>(), Mock.Of<IMessageBus>(), ct);
        var ownerResponse = await UpdateRoasterHandler.Handle(new UpdateRoasterCommand(roaster.Id, "Owner edit", ActorUserId: owner)
            { ExpectedRevision = 1, OwnerUserId = owner }, new RoasterRepository(ownerDb), mapper.Object,
            Mock.Of<IUploadedPhotoLookup>(), Mock.Of<IMessageBus>(), ct);
        Assert.True(adminResponse.IsSuccess);
        Assert.True(ownerResponse.IsSuccess);

        var winner = adminFirst ? adminDb : ownerDb;
        var loser = adminFirst ? ownerDb : adminDb;
        await winner.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<ConflictException>(() => loser.SaveChangesAsync(ct));

        await using var verify = fixture.Context();
        var persisted = await verify.Roasters.AsNoTracking().SingleAsync(r => r.Id == roaster.Id, ct);
        Assert.Equal(adminFirst ? "Admin edit" : "Owner edit", persisted.Name);
        Assert.Equal(owner, persisted.OwnerUserId);
        Assert.Equal(2, persisted.ProfileRevision);
    }

    [Fact]
    public async Task RevocationCommittedAfterOwnerReadPreventsOwnerProfileCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var roaster = await SeedAsync(owner, ct);
        await using var ownerDb = fixture.Context();
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<RoasterDto>(It.IsAny<Roaster>())).Returns(new RoasterDto());
        var response = await UpdateRoasterHandler.Handle(new UpdateRoasterCommand(roaster.Id, "Forbidden stale edit", ActorUserId: owner)
            { ExpectedRevision = 1, OwnerUserId = owner }, new RoasterRepository(ownerDb), mapper.Object,
            Mock.Of<IUploadedPhotoLookup>(), Mock.Of<IMessageBus>(), ct);
        Assert.True(response.IsSuccess);

        await using (var revokeDb = fixture.Context())
        {
            await new RoasterOwnershipWriter(new RoasterRepository(revokeDb)).AssignAsync(roaster.Id, null, false, ct);
            await revokeDb.SaveChangesAsync(ct);
        }

        await Assert.ThrowsAsync<ConflictException>(() => ownerDb.SaveChangesAsync(ct));
        await using var verify = fixture.Context();
        var persisted = await verify.Roasters.AsNoTracking().SingleAsync(r => r.Id == roaster.Id, ct);
        Assert.Equal(roaster.Name, persisted.Name);
        Assert.Null(persisted.OwnerUserId);
        Assert.Equal(2, persisted.ProfileRevision);
    }

    private async Task<Roaster> SeedAsync(Guid owner, CancellationToken ct)
    {
        await using var db = fixture.Context();
        var roaster = new Roaster("Ownership revision " + Guid.NewGuid().ToString("N"));
        roaster.AssignOwner(owner);
        db.Roasters.Add(roaster);
        await db.SaveChangesAsync(ct);
        return roaster;
    }
}
