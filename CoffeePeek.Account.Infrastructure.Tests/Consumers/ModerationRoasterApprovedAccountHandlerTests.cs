using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Infrastructure.Consumers;
using CoffeePeek.Account.Infrastructure.Tests.Persistence;
using CoffeePeek.Account.Persistence.Profiles;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Moderation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Account.Infrastructure.Tests.Consumers;

public sealed class ModerationRoasterApprovedAccountHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RepeatedAndDistinctRoasterApprovals_CreditEachRoasterOnlyOnce()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var author = ProfileSocialStoreTests.NewUser("RoasterAuthor");
        db.Add(author);
        await db.SaveChangesAsync(Ct);
        var handler = new ModerationRoasterApprovedAccountHandler(new RoasterContributionStore(db));
        var approval = new ModerationRoasterApprovedEvent(author.Id, new ModerationRoasterDto { Id = Guid.NewGuid(), Name = "Roaster" });

        await handler.Handle(approval, Ct);
        await handler.Handle(approval, Ct);
        await handler.Handle(approval with { Roaster = approval.Roaster with { Id = Guid.NewGuid() } }, Ct);

        (await db.UserRoasterCredits.CountAsync(Ct)).Should().Be(2);
        db.ChangeTracker.Clear();
        (await db.Users.SingleAsync(u => u.Id == author.Id, Ct)).Statistics.AddedRoastersCount.Should().Be(2);
    }

    [Fact]
    public async Task UnknownDeletedAndAnonymousAuthors_AreHarmless()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var deleted = ProfileSocialStoreTests.NewUser("DeletedAuthor");
        deleted.SetSoftDelete();
        db.Add(deleted);
        await db.SaveChangesAsync(Ct);
        var handler = new ModerationRoasterApprovedAccountHandler(new RoasterContributionStore(db));
        foreach (var authorId in new Guid?[] { null, Guid.NewGuid(), deleted.Id, Guid.Empty })
            await handler.Handle(new(authorId, new ModerationRoasterDto { Id = Guid.NewGuid(), Name = "Roaster" }), Ct);

        db.UserRoasterCredits.Should().BeEmpty();
        deleted.Statistics.AddedRoastersCount.Should().Be(0);
    }

    [Fact]
    public async Task Handler_ForwardsAuthorRoasterAndCancellation()
    {
        var store = new Mock<IRoasterContributionStore>();
        var message = new ModerationRoasterApprovedEvent(Guid.NewGuid(), new ModerationRoasterDto { Id = Guid.NewGuid(), Name = "Roaster" });
        await new ModerationRoasterApprovedAccountHandler(store.Object).Handle(message, Ct);
        store.Verify(s => s.CreditAsync(message.UserId!.Value, message.Roaster.Id, Ct), Times.Once);
    }
}
