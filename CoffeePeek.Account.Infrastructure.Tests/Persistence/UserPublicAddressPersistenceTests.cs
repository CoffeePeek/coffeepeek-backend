using CoffeePeek.Account.Domain.Entities.PublicAddressAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Shared.Domain.PublicAddresses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins the Account-local user registry to the Shops registry schema (D-05) and the tombstone privacy rule (D-03).
/// Reads the Npgsql design-time model; no database connection is opened.
/// </summary>
public sealed class UserPublicAddressPersistenceTests
{
    private const string Table = "UserPublicAddresses";

    // Parity with Shops is structural: both configurations use CoffeePeek.Shared.Domain PublicAddressChecks.
    // These literals pin the exact SQL so a change to the shared constants is a deliberate, migrated decision.
    private const string SlugSql = "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100";
    private const string LifecycleSql = "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)";
    private const string RevisionSql = "\"Revision\" > 0";
    private const string TimestampsSql = "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"";

    [Fact]
    public void Model_MapsSingleKindRegistryTableWithoutInheritance()
    {
        using var db = CreateNpgsqlDb();
        var entity = Registry(db);

        entity.GetTableName().Should().Be(Table);
        entity.BaseType.Should().BeNull();
    }

    [Fact]
    public void Model_HasOnlyTheSevenBaseColumnsAndNoNavigation()
    {
        using var db = CreateNpgsqlDb();
        var entity = Registry(db);

        entity.GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            nameof(PublicAddress.Id),
            nameof(PublicAddress.OwnerId),
            nameof(PublicAddress.Slug),
            nameof(PublicAddress.State),
            nameof(PublicAddress.Revision),
            nameof(PublicAddress.CreatedAtUtc),
            nameof(PublicAddress.UpdatedAtUtc));
        entity.GetNavigations().Should().BeEmpty();
        entity.GetSkipNavigations().Should().BeEmpty();
    }

    [Fact]
    public void Model_SlugIsBinaryCollatedAndBoundedAndRevisionIsConcurrencyToken()
    {
        using var db = CreateNpgsqlDb();
        var entity = Registry(db);

        var slug = entity.FindProperty(nameof(PublicAddress.Slug))!;
        slug.GetMaxLength().Should().Be(PublicSlug.MaxLength).And.Be(100);
        slug.GetCollation().Should().Be("C");
        slug.IsNullable.Should().BeFalse();
        entity.FindProperty(nameof(PublicAddress.Revision))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void Model_ReservesEverySlugAndAllowsAtMostOneCanonicalPerOwner()
    {
        using var db = CreateNpgsqlDb();
        var entity = Registry(db);
        var indexes = entity.GetIndexes().ToArray();

        var slug = indexes.Should().ContainSingle(i => Columns(i).SequenceEqual(new[] { nameof(PublicAddress.Slug) })).Which;
        slug.IsUnique.Should().BeTrue();
        slug.GetFilter().Should().BeNull();

        var canonical = indexes.Should().ContainSingle(i => Columns(i).SequenceEqual(new[] { nameof(PublicAddress.OwnerId) })).Which;
        canonical.IsUnique.Should().BeTrue();
        canonical.GetFilter().Should().Be("\"State\" = 1");

        var ownerState = indexes.Should().ContainSingle(i =>
            Columns(i).SequenceEqual(new[] { nameof(PublicAddress.OwnerId), nameof(PublicAddress.State) })).Which;
        ownerState.IsUnique.Should().BeFalse();

        indexes.Should().HaveCount(3);
    }

    [Fact]
    public void Model_RestrictsUserDeletionUntilRetiredWithoutNavigations()
    {
        using var db = CreateNpgsqlDb();
        var entity = Registry(db);

        var foreignKey = entity.GetForeignKeys().Should().ContainSingle().Which;
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        foreignKey.PrincipalEntityType.ClrType.Should().Be(typeof(User));
        foreignKey.Properties.Select(p => p.Name).Should().Equal(nameof(PublicAddress.OwnerId));
        foreignKey.DependentToPrincipal.Should().BeNull();
        foreignKey.PrincipalToDependent.Should().BeNull();
    }

    [Fact]
    public void Model_CheckConstraintsMatchShopsRegistriesExactly()
    {
        using var db = CreateNpgsqlDb();
        var checks = Registry(db).GetCheckConstraints().ToDictionary(c => c.Name!, c => c.Sql);

        checks.Keys.Should().BeEquivalentTo(
            $"CK_{Table}_Slug", $"CK_{Table}_Lifecycle", $"CK_{Table}_Revision", $"CK_{Table}_Timestamps");
        checks[$"CK_{Table}_Slug"].Should().Be(SlugSql);
        checks[$"CK_{Table}_Lifecycle"].Should().Be(LifecycleSql);
        checks[$"CK_{Table}_Revision"].Should().Be(RevisionSql);
        checks[$"CK_{Table}_Timestamps"].Should().Be(TimestampsSql);
    }

    [Fact]
    public void Model_UserHasNoNavigationToRegistry()
    {
        using var db = CreateNpgsqlDb();
        var user = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(User))!;

        user.GetNavigations().Should().NotContain(n => n.TargetEntityType.ClrType == typeof(UserPublicAddress));
        user.GetSkipNavigations().Should().NotContain(n => n.TargetEntityType.ClrType == typeof(UserPublicAddress));
        user.GetReferencingForeignKeys().Should().ContainSingle(fk => fk.DeclaringEntityType.ClrType == typeof(UserPublicAddress))
            .Which.PrincipalToDependent.Should().BeNull();
    }

    private static IEntityType Registry(AccountDbContext db) =>
        db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(UserPublicAddress))
        ?? throw new InvalidOperationException("UserPublicAddress is not mapped.");

    private static IEnumerable<string> Columns(IIndex index) => index.Properties.Select(p => p.Name);

    private static AccountDbContext CreateNpgsqlDb() => new(new DbContextOptionsBuilder<AccountDbContext>()
        .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
}
