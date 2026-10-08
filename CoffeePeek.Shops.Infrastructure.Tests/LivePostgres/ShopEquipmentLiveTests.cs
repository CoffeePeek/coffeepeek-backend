using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopEquipmentLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase _fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ShopEquipmentLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        _fixture = fixture;
    }

    [Fact]
    public async Task SharedCatalogEquipmentHasIndependentPrimaryAssignmentPerShop()
    {
        var city = new City("equipment-city-" + Guid.NewGuid().ToString("N"));
        var category = new EquipmentCategory();
        var first = default(Equipment)!;
        var second = default(Equipment)!;
        await using (var seed = _fixture.Context(false))
        {
            Assert.False(seed.Database.HasPendingModelChanges());
            seed.Cities.Add(city);
            seed.EquipmentCategories.Add(category);
            seed.Entry(category).Property(x => x.Name).CurrentValue = "equipment-category-" + Guid.NewGuid().ToString("N");
            await seed.SaveChangesAsync(Ct);

            first = new Equipment("Primary", "A", category);
            second = new Equipment("Primary", "B", category);
            seed.Equipments.AddRange(first, second);
            await seed.SaveChangesAsync(Ct);

            var shopA = ShopsLiveDatabase.NewShop("equipment-shop-A", city.Id);
            var shopB = ShopsLiveDatabase.NewShop("equipment-shop-B", city.Id);
            Assert.True(shopA.SetEquipment([first, second], [first.Id]).IsSuccess);
            Assert.True(shopB.SetEquipment([first, second], [second.Id]).IsSuccess);
            seed.Shops.AddRange(shopA, shopB);
            await seed.SaveChangesAsync(Ct);

            await using var verify = _fixture.Context(false);
            var persisted = await verify.Shops.Include(s => s.ShopEquipments)
                .Where(s => s.Id == shopA.Id || s.Id == shopB.Id).ToListAsync(Ct);
            Assert.Equal(first.Id, persisted.Single(s => s.Id == shopA.Id)
                .ShopEquipments.Single(a => a.IsPrimary).EquipmentId);
            Assert.Equal(second.Id, persisted.Single(s => s.Id == shopB.Id)
                .ShopEquipments.Single(a => a.IsPrimary).EquipmentId);
        }
    }

    [Fact]
    public async Task DatabaseRejectsSecondPrimaryInSameShopCategory()
    {
        var city = new City("equip-conflict-" + Guid.NewGuid().ToString("N"));
        var category = new EquipmentCategory();
        await using var db = _fixture.Context(false);
        db.Cities.Add(city);
        db.EquipmentCategories.Add(category);
        db.Entry(category).Property(x => x.Name).CurrentValue = "equipment-conflict-category-" + Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(Ct);
        var first = new Equipment("Conflict", "A", category);
        var second = new Equipment("Conflict", "B", category);
        db.Equipments.AddRange(first, second);
        var shop = ShopsLiveDatabase.NewShop("equipment-conflict-shop", city.Id);
        Assert.True(shop.SetEquipment([first, second], [first.Id]).IsSuccess);
        db.Shops.Add(shop);
        await db.SaveChangesAsync(Ct);

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"CoffeeShopEquipments\" SET \"IsPrimary\" = TRUE WHERE \"CoffeeShopId\" = {shop.Id} AND \"EquipmentId\" = {second.Id}", Ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("UX_CoffeeShopEquipments_PrimaryCategory", error.ConstraintName);
    }

    [Fact]
    public async Task SwitchingPrimaryClearsOldChoiceWithinSingleTransaction()
    {
        var city = new City("equip-switch-" + Guid.NewGuid().ToString("N"));
        var category = new EquipmentCategory();
        var shopId = Guid.Empty;
        var nextId = Guid.Empty;
        await using (var seed = _fixture.Context(false))
        {
            seed.Cities.Add(city);
            seed.EquipmentCategories.Add(category);
            seed.Entry(category).Property(x => x.Name).CurrentValue = "equipment-switch-category-" + Guid.NewGuid().ToString("N");
            await seed.SaveChangesAsync(Ct);
            var first = new Equipment("Switch", "A", category);
            var second = new Equipment("Switch", "B", category);
            seed.Equipments.AddRange(first, second);
            var shop = ShopsLiveDatabase.NewShop("equipment-switch-shop", city.Id);
            Assert.True(shop.SetEquipment([first, second], [first.Id]).IsSuccess);
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(Ct);
            shopId = shop.Id;
            nextId = second.Id;
        }

        await using (var update = _fixture.Context(false))
        await using (var tx = await update.Database.BeginTransactionAsync(Ct))
        {
            var repository = new CoffeeShopRepository(update);
            var shop = await repository.GetByIdWithCatalogsAsync(shopId, Ct);
            Assert.NotNull(shop);
            var previousId = shop.ShopEquipments.Single(a => a.IsPrimary).EquipmentId;
            await repository.ClearSupersededPrimaryEquipmentAsync(shopId, [previousId], Ct);
            Assert.True(shop.SetEquipment(shop.Equipments, [nextId]).IsSuccess);
            await update.SaveChangesAsync(Ct);
            await tx.CommitAsync(Ct);
        }

        await using var verify = _fixture.Context(false);
        var choices = await verify.ShopEquipments.Where(a => a.ShopId == shopId).ToListAsync(Ct);
        Assert.Equal(nextId, choices.Single(a => a.IsPrimary).EquipmentId);
    }

    [Fact]
    public async Task LegacyJoinInsert_FillsCategoryAndCatalogFlagRemainsReadable()
    {
        var city = new City("equip-legacy-" + Guid.NewGuid().ToString("N"));
        var category = new EquipmentCategory();
        Guid shopId;
        Guid equipmentId;
        int categoryId;
        await using (var seed = _fixture.Context(false))
        {
            seed.Cities.Add(city);
            seed.EquipmentCategories.Add(category);
            seed.Entry(category).Property(x => x.Name).CurrentValue = "equipment-legacy-category-" + Guid.NewGuid().ToString("N");
            await seed.SaveChangesAsync(Ct);
            var equipment = new Equipment("Legacy", "A", category);
            var shop = ShopsLiveDatabase.NewShop("equipment-legacy-shop", city.Id);
            seed.Equipments.Add(equipment);
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(Ct);
            shopId = shop.Id;
            equipmentId = equipment.Id;
            categoryId = category.Id;
        }

        await using (var legacy = _fixture.Context(false))
        {
            // This is the INSERT shape emitted by the previous binary's many-to-many mapping.
            await legacy.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"CoffeeShopEquipments\" (\"CoffeeShopId\", \"EquipmentId\") VALUES ({shopId}, {equipmentId})", Ct);
            var assignment = await legacy.ShopEquipments.SingleAsync(
                x => x.ShopId == shopId && x.EquipmentId == equipmentId, Ct);
            Assert.Equal(categoryId, assignment.CategoryId);
            Assert.False(assignment.IsPrimary);
        }

        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT \"IsPrimary\" FROM \"Equipments\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", equipmentId);
        Assert.False((bool)(await command.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task AssignedEquipmentCannotChangeCatalogCategory()
    {
        var city = new City("equip-category-" + Guid.NewGuid().ToString("N"));
        var original = new EquipmentCategory();
        var replacement = new EquipmentCategory();
        Guid equipmentId;
        await using (var seed = _fixture.Context(false))
        {
            seed.Cities.Add(city);
            seed.EquipmentCategories.AddRange(original, replacement);
            seed.Entry(original).Property(x => x.Name).CurrentValue = "original-" + Guid.NewGuid().ToString("N");
            seed.Entry(replacement).Property(x => x.Name).CurrentValue = "replacement-" + Guid.NewGuid().ToString("N");
            await seed.SaveChangesAsync(Ct);

            var equipment = new Equipment("Assigned", "A", original);
            var shop = ShopsLiveDatabase.NewShop("assigned-equipment-shop", city.Id);
            Assert.True(shop.SetEquipment([equipment], [equipment.Id]).IsSuccess);
            seed.Equipments.Add(equipment);
            seed.Shops.Add(shop);
            await seed.SaveChangesAsync(Ct);
            equipmentId = equipment.Id;
        }

        await using var update = _fixture.Context(false);
        var repository = new EquipmentRepository(update);
        Assert.True(await repository.HasShopAssignmentsAsync(equipmentId, Ct));
        var persisted = await repository.GetByIdAsync(equipmentId, Ct);
        Assert.NotNull(persisted);
        persisted.Update(persisted.Brand, persisted.ModelName,
            await update.EquipmentCategories.SingleAsync(x => x.Id == replacement.Id, Ct));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => update.SaveChangesAsync(Ct));
        Assert.True(ShopEquipmentConstraintViolation.Matches(error));
    }
}
