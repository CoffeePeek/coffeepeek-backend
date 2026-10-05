using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

public sealed class UserSlugTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private static AccountDbContext Db()=>new(new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Microsoft.Extensions.Options.Options.Create(new PublicAddressOptions { Enabled=true }));
    private static User NewUser(string username)=>User.Register(Guid.NewGuid()+"@example.com",username,"hash",Role.Create("User"));

    [Fact]
    public async Task UsernameChange_ReplacesSlugAndOldAddressReturnsNotFound()
    {
        await using var db=Db();var user=NewUser("Ivan.Petrov");db.Add(user);await db.SaveChangesAsync(Ct);
        var service=new ProfileAddressService(db);user.PublicSlug.Should().Be("ivan-petrov");
        user.UpdateUsername(Username.Create("Petr"));await db.SaveChangesAsync(Ct);
        user.PublicSlug.Should().Be("petr");user.SlugRevision.Should().Be(2);
        (await service.ResolveAsync("ivan-petrov",Ct)).Should().BeNull();
        var current=await service.ResolveAsync("PETR",Ct);current!.EntityId.Should().Be(user.Id);current.IsAlias.Should().BeFalse();
    }

    [Fact]
    public async Task SlugCollision_UsesSuffix_CaseOnlyChangeKeepsCurrentSlug()
    {
        await using var db=Db();var a=NewUser("Ivan");var b=NewUser("ivan");db.AddRange(a,b);await db.SaveChangesAsync(Ct);
        a.PublicSlug.Should().Be("ivan");b.PublicSlug.Should().Be(PublicSlug.Candidate("ivan",b.Id,1));
        var before=b.PublicSlug;b.UpdateUsername(Username.Create("IVAN"));await db.SaveChangesAsync(Ct);
        b.PublicSlug.Should().Be(before);b.SlugRevision.Should().Be(1);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("User")]
    [InlineData("日本語")]
    public async Task UnsupportedOrReservedUsername_GetsUserFallback(string username)
    {
        await using var db=Db();var user=NewUser(username);db.Add(user);await db.SaveChangesAsync(Ct);
        user.PublicSlug.Should().Be(PublicSlug.Candidate("user",user.Id,1));
    }

    [Fact]
    public async Task BlockedAndDeletedProfiles_AreHidden_AndDeletionReleasesSlug()
    {
        await using var db=Db();var user=NewUser("Visible");db.Add(user);await db.SaveChangesAsync(Ct);
        var service=new ProfileAddressService(db);user.SetBlocked(true);await db.SaveChangesAsync(Ct);
        (await service.ResolveAsync("visible",Ct)).Should().BeNull();
        (await service.GetByIdsAsync([user.Id],Ct)).Should().BeEmpty();
        user.SetBlocked(false);await db.SaveChangesAsync(Ct);(await service.ResolveAsync("visible",Ct)).Should().NotBeNull();
        db.Remove(user);await db.SaveChangesAsync(Ct);(await service.ResolveAsync("visible",Ct)).Should().BeNull();
        var other=NewUser("Visible");db.Add(other);await db.SaveChangesAsync(Ct);other.PublicSlug.Should().Be("visible");
    }

    [Fact]
    public void Model_ContainsUniqueSlugOnUsersOnly()
    {
        using var db=new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=x").Options);
        var user=db.Model.FindEntityType(typeof(User))!;
        user.GetIndexes().Should().Contain(i=>i.IsUnique&&i.Properties.Any(p=>p.Name==nameof(User.PublicSlug)));
        user.FindProperty(nameof(User.SlugRevision))!.IsConcurrencyToken.Should().BeTrue();
        db.Model.GetEntityTypes().Should().NotContain(e=>e.Name.Contains("PublicAddressAggregate"));
    }
}
