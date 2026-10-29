using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Ownership;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class BusinessOwnershipLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public BusinessOwnershipLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(BusinessType.CoffeeShop)]
    [InlineData(BusinessType.Roaster)]
    public async Task SubmitApproveRevokeReplay_CommitsAccessAndPreservesHistory(BusinessType kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, slug) = await SeedBusiness(kind, ct);
        var applicant = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var evidencePhotos = new Mock<IOwnershipEvidencePhotos>(MockBehavior.Strict);
        evidencePhotos.Setup(x => x.AttachAsync(applicant, It.IsAny<Guid>(),
                It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { photoId })), It.IsAny<CancellationToken>(), null))
            .Returns(Task.CompletedTask);
        using var host = CreateHost(id, slug, evidencePhotos: evidencePhotos.Object);
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var submitted = await bus.InvokeAsync<Response<OwnershipClaimDto>>(
                new SubmitOwnershipClaimCommand(applicant, kind, slug, "owner@example.test", "Official account access", [photoId]), ct);
            var claimId = submitted.Data!.Id;
            Assert.False(submitted.Data.HasCurrentAccess);
            Assert.Equal(new[] { photoId }, submitted.Data.EvidencePhotoIds);
            evidencePhotos.Verify(x => x.AttachAsync(applicant, claimId,
                It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { photoId })), It.IsAny<CancellationToken>(), null), Times.Once);
            var approve = new ReviewOwnershipClaimCommand(claimId, admin, true);
            var approved = await bus.InvokeAsync<Response<OwnershipClaimDto>>(approve, ct);
            Assert.True(approved.Data!.HasCurrentAccess);
            await bus.InvokeAsync<Response<OwnershipClaimDto>>(approve, ct);
            await bus.InvokeAsync<Response<BusinessOwnershipTargetDto>>(
                new AssignBusinessOwnerCommand(kind, id, null, admin, "Authority ended"), ct);
            var replay = await bus.InvokeAsync<Response<OwnershipClaimDto>>(approve, ct);
            Assert.Equal(OwnershipClaimStatus.Approved, replay.Data!.Status);
            Assert.False(replay.Data.HasCurrentAccess);

            await using var read = fixture.Context(false);
            Assert.Equal(new[] { photoId },
                (await read.BusinessOwnershipClaims.SingleAsync(x => x.Id == claimId, ct)).EvidencePhotoIds);
            Assert.Null((await new BusinessOwnershipStore(read).GetTargetAsync(kind, id, ct))!.OwnerUserId);
            var changes = await read.BusinessOwnershipChanges.Where(x => x.BusinessId == id)
                .OrderBy(x => x.CreatedAtUtc).ToArrayAsync(ct);
            Assert.Equal(2, changes.Length);
            Assert.Equal(applicant, changes[0].NewOwnerUserId);
            Assert.Equal(claimId, changes[0].ClaimId);
            Assert.Null(changes[1].NewOwnerUserId);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(BusinessType.CoffeeShop)]
    [InlineData(BusinessType.Roaster)]
    public async Task RejectedClaimCanBeResubmittedWithTheSamePhotosWithoutReplacingHistory(BusinessType kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, slug) = await SeedBusiness(kind, ct);
        var applicant = Guid.NewGuid(); var photo = Guid.NewGuid();
        var evidence = new Mock<IOwnershipEvidencePhotos>(MockBehavior.Strict);
        evidence.Setup(x => x.AttachAsync(applicant, It.IsAny<Guid>(), It.IsAny<Guid[]>(),
            It.IsAny<CancellationToken>(), It.IsAny<Guid?>())).Returns(Task.CompletedTask);
        using var host = CreateHost(id, slug, evidencePhotos: evidence.Object);
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var command = new SubmitOwnershipClaimCommand(applicant, kind, slug, "contact", "proof", [photo]);
            var first = (await bus.InvokeAsync<Response<OwnershipClaimDto>>(command, ct)).Data!;
            await bus.InvokeAsync<Response<OwnershipClaimDto>>(
                new ReviewOwnershipClaimCommand(first.Id, Guid.NewGuid(), false, "More proof required"), ct);
            var next = (await bus.InvokeAsync<Response<OwnershipClaimDto>>(command, ct)).Data!;
            Assert.NotEqual(first.Id, next.Id);
            Assert.False(next.HasCurrentAccess);
            evidence.Verify(x => x.AttachAsync(applicant, next.Id, It.Is<Guid[]>(ids => ids.SequenceEqual(new[] { photo })),
                It.IsAny<CancellationToken>(), first.Id), Times.Once);
            await Assert.ThrowsAsync<ConflictException>(() => bus.InvokeAsync<Response<OwnershipClaimDto>>(command, ct));
            await using var read = fixture.Context(false);
            var claims = await read.BusinessOwnershipClaims.Where(x => x.BusinessId == id && x.ApplicantUserId == applicant).ToArrayAsync(ct);
            Assert.Equal(2, claims.Length);
            Assert.Equal(OwnershipClaimStatus.Rejected, claims.Single(x => x.Id == first.Id).Status);
            Assert.Equal(OwnershipClaimStatus.Pending, claims.Single(x => x.Id == next.Id).Status);
            Assert.All(claims, claim => Assert.Equal(new[] { photo }, claim.EvidencePhotoIds));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(BusinessType.CoffeeShop, false)]
    [InlineData(BusinessType.Roaster, false)]
    [InlineData(BusinessType.CoffeeShop, true)]
    [InlineData(BusinessType.Roaster, true)]
    public async Task Approval_FlushIsInvisibleUntilCommit_AndLateFailureRollsBack(BusinessType kind, bool fail)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, slug) = await SeedBusiness(kind, ct);
        var claim = BusinessOwnershipClaim.Create(kind, id, slug, slug, Guid.NewGuid(), "contact", "evidence");
        await using (var seed = fixture.Context(false))
        {
            seed.BusinessOwnershipClaims.Add(claim);
            await seed.SaveChangesAsync(ct);
        }
        var observed = false;
        var probe = new AfterFlushProbe(async db =>
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            await using var read = fixture.Context(false);
            Assert.Null((await new BusinessOwnershipStore(read).GetTargetAsync(kind, id, ct))!.OwnerUserId);
            Assert.Equal(OwnershipClaimStatus.Pending,
                (await read.BusinessOwnershipClaims.SingleAsync(x => x.Id == claim.Id, ct)).Status);
            Assert.False(await read.BusinessOwnershipChanges.AnyAsync(x => x.BusinessId == id, ct));
            observed = true;
            if (fail) throw new InvalidOperationException("Ownership failure after flush");
        });
        using var host = CreateHost(id, slug, probe);
        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var command = new ReviewOwnershipClaimCommand(claim.Id, Guid.NewGuid(), true);
            if (fail)
                await Assert.ThrowsAsync<InvalidOperationException>(() => bus.InvokeAsync<Response<OwnershipClaimDto>>(command, ct));
            else
                await bus.InvokeAsync<Response<OwnershipClaimDto>>(command, ct);
            Assert.True(observed);
            await using var read = fixture.Context(false);
            Assert.Equal(fail ? null : (Guid?)claim.ApplicantUserId,
                (await new BusinessOwnershipStore(read).GetTargetAsync(kind, id, ct))!.OwnerUserId);
            Assert.Equal(fail ? OwnershipClaimStatus.Pending : OwnershipClaimStatus.Approved,
                (await read.BusinessOwnershipClaims.SingleAsync(x => x.Id == claim.Id, ct)).Status);
            Assert.Equal(fail ? 0 : 1, await read.BusinessOwnershipChanges.CountAsync(x => x.BusinessId == id, ct));
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(BusinessType.CoffeeShop)]
    [InlineData(BusinessType.Roaster)]
    public async Task CompetingApprovals_OnlyOneOwnerAndDecisionPersist(BusinessType kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, slug) = await SeedBusiness(kind, ct);
        var first = BusinessOwnershipClaim.Create(kind, id, slug, slug, Guid.NewGuid(), "first", "evidence");
        var second = BusinessOwnershipClaim.Create(kind, id, slug, slug, Guid.NewGuid(), "second", "evidence");
        await using (var seed = fixture.Context(false))
        {
            seed.BusinessOwnershipClaims.AddRange(first, second);
            await seed.SaveChangesAsync(ct);
        }
        await using var left = fixture.Context(false);
        await using var right = fixture.Context(false);
        await Review(left, first.Id, true, ct);
        await Review(right, second.Id, true, ct);
        await left.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<ConflictException>(() => right.SaveChangesAsync(ct));
        await using var read = fixture.Context(false);
        Assert.Equal(first.ApplicantUserId, (await new BusinessOwnershipStore(read).GetTargetAsync(kind, id, ct))!.OwnerUserId);
        Assert.Equal(OwnershipClaimStatus.Pending, (await read.BusinessOwnershipClaims.SingleAsync(x => x.Id == second.Id, ct)).Status);
        Assert.Single(await read.BusinessOwnershipChanges.Where(x => x.BusinessId == id).ToArrayAsync(ct));
    }

    [Fact]
    public async Task DuplicatePendingAndCompetingDecisions_ReturnConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, slug) = await SeedBusiness(BusinessType.Roaster, ct);
        var first = BusinessOwnershipClaim.Create(BusinessType.Roaster, id, slug, slug, Guid.NewGuid(), "contact", "evidence");
        await using (var seed = fixture.Context(false))
        {
            seed.BusinessOwnershipClaims.Add(first);
            await seed.SaveChangesAsync(ct);
        }
        await using (var duplicate = fixture.Context(false))
        {
            duplicate.BusinessOwnershipClaims.Add(BusinessOwnershipClaim.Create(BusinessType.Roaster,
                id, slug, slug, first.ApplicantUserId, "contact", "evidence"));
            await Assert.ThrowsAsync<ConflictException>(() => duplicate.SaveChangesAsync(ct));
        }
        await using var approve = fixture.Context(false);
        await using var reject = fixture.Context(false);
        await Review(approve, first.Id, true, ct);
        await Review(reject, first.Id, false, ct);
        await approve.SaveChangesAsync(ct);
        await Assert.ThrowsAsync<ConflictException>(() => reject.SaveChangesAsync(ct));
    }

    private static Task<Response<OwnershipClaimDto>> Review(ShopsDbContext db, Guid claimId, bool approve, CancellationToken ct)
    {
        var store = new BusinessOwnershipStore(db);
        var service = new BusinessOwnershipService(store, new CoffeeShopRepository(db),
            new RoasterOwnershipWriter(new RoasterRepository(db)), ExistingUser(), Mock.Of<IMessageBus>());
        return OwnershipClaimHandler.Handle(new ReviewOwnershipClaimCommand(claimId, Guid.NewGuid(), approve, "Checked"),
            store, service, new UnitOfWork<ShopsDbContext>(db), ct);
    }

    private async Task<(Guid Id, string Slug)> SeedBusiness(BusinessType kind, CancellationToken ct)
    {
        var slug = "ownership-" + Guid.NewGuid().ToString("N");
        await using var db = fixture.Context(false);
        if (kind == BusinessType.Roaster)
        {
            var roaster = new Roaster(slug);
            roaster.SetPublicSlug(slug);
            db.Roasters.Add(roaster);
            await db.SaveChangesAsync(ct);
            return (roaster.Id, slug);
        }
        var city = new City(slug);
        var shop = ShopsLiveDatabase.NewShop(slug, city.Id);
        shop.SetPublicSlug(slug);
        db.Cities.Add(city);
        db.Shops.Add(shop);
        await db.SaveChangesAsync(ct);
        return (shop.Id, slug);
    }

    private static IUserExistenceLookup ExistingUser() =>
        Mock.Of<IUserExistenceLookup>(x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true));

    private IHost CreateHost(Guid businessId, string slug, SaveChangesInterceptor? probe = null,
        IOwnershipEvidencePhotos? evidencePhotos = null) => Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddScoped(_ => probe is null ? fixture.Context(false) : new ShopsDbContext(
                new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(fixture.ConnectionString)
                    .AddInterceptors(new AuditInterceptor(), probe).Options));
            services.AddScoped<IUnitOfWork, UnitOfWork<ShopsDbContext>>();
            services.AddScoped<IBusinessOwnershipStore, BusinessOwnershipStore>();
            services.AddScoped<BusinessOwnershipService>();
            services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
            services.AddScoped<IRoasterRepository, RoasterRepository>();
            services.AddScoped<IRoasterOwnershipWriter, RoasterOwnershipWriter>();
            services.AddSingleton(evidencePhotos ?? new Mock<IOwnershipEvidencePhotos>(MockBehavior.Strict).Object);
            services.AddSingleton(ExistingUser());
            services.AddSingleton(Mock.Of<IPublicAddressService>(x => x.ResolveAsync(It.IsAny<PublicAddressKind>(),
                slug, It.IsAny<CancellationToken>()) == Task.FromResult<PublicAddressMetadata?>(new(businessId, slug, "/test/" + slug, 1))));
            var cache = new Mock<IStrictCacheInvalidator>();
            cache.Setup(x => x.RemoveAsync(It.IsAny<CacheKey>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            cache.Setup(x => x.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
            services.AddSingleton(cache.Object);
        })
        .UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(BusinessOwnershipLiveTests).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(OwnershipClaimHandler));
            options.Discovery.IncludeType(typeof(ShopProfileCacheInvalidationRequestedHandler));
            options.UseRuntimeCompilation();
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IPublicAddressService));
            options.PersistMessagesWithPostgresql(fixture.ConnectionString, "ownership_wolverine");
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            options.Policies.UseDurableLocalQueues();
            options.Durability.Mode = DurabilityMode.Solo;
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
        }).Build();

    private sealed class AfterFlushProbe(Func<DbContext, Task> observe) : SaveChangesInterceptor
    {
        private int calls;
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 1) await observe(eventData.Context!);
            return result;
        }
    }
}
