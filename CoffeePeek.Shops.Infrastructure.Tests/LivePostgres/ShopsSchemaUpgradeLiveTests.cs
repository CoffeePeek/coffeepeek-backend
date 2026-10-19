using System.Text.Json.Nodes;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class ShopsSchemaUpgradeLiveTests
{
    private const string Previous = "20261008090000_AddMenuOcrJobs";

    [Theory]
    [InlineData("valid")]
    [InlineData("city-orphan")]
    [InlineData("menu-invalid")]
    [InlineData("menu-missing-price")]
    [InlineData("menu-missing-volume")]
    [InlineData("menu-absent-values")]
    [InlineData("menu-unknown-values")]
    [InlineData("menu-invalid-availability")]
    public async Task OldPopulatedSchemaPreservesValidDataRepairsMenuValuesAndRejectsCityOrphans(string scenario)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var connection = await LivePostgresServer.CreateDatabaseAsync("shopsupgrade", ct);
        try
        {
            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
            await db.GetService<IMigrator>().MigrateAsync(Previous, ct);
            await SeedAsync(db, ct);
            if (scenario == "city-orphan")
                await db.Database.ExecuteSqlRawAsync("UPDATE \"Shops\" SET \"CityId\"=gen_random_uuid()", ct);
            var menuEdit = scenario switch
            {
                "menu-invalid" => "\"Price\"=0",
                "menu-missing-price" => "\"Price\"=NULL",
                "menu-missing-volume" => "\"VolumeMl\"=NULL",
                "menu-absent-values" => "\"Availability\"=2",
                "menu-unknown-values" => "\"Availability\"=0",
                "menu-invalid-availability" => "\"Availability\"=99",
                _ => null
            };
            if (menuEdit != null)
                await db.Database.ExecuteSqlRawAsync("UPDATE \"ShopMenuItems\" SET " + menuEdit, ct);
            var before = await ArchiveAsync(db, ct);
            var expected = JsonNode.Parse(before)!;
            if (menuEdit != null)
            {
                var item = expected["items"]![0]!;
                item["Availability"] = 0;
                item["Price"] = null;
                expected["shops"]![0]!["DataCompletenessScore"] = 5;
            }
            if (scenario == "city-orphan")
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync(ct));
                Assert.Contains("City reference preflight failed", error.MessageText);
                Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(await ArchiveAsync(db, ct))));
                Assert.DoesNotContain("20261008091000_ProtectCityReferences", await db.Database.GetAppliedMigrationsAsync(ct));
                return;
            }
            await db.Database.MigrateAsync(ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(await ArchiveAsync(db, ct))));
            Assert.Equal(2, await db.ShopEquipments.CountAsync(ct));
            Assert.All(await db.ShopEquipments.ToArrayAsync(ct), a => { Assert.False(a.IsPrimary); Assert.Equal(500, a.CategoryId); });
            Assert.All(await db.Shops.ToArrayAsync(ct), s => Assert.Equal(0, s.ProfileRevision));
            Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT bool_and(\"IsPrimary\") AS \"Value\" FROM \"Equipments\"").SingleAsync(ct));
            var script = db.GetService<IMigrator>().GenerateScript(Previous, null, MigrationsSqlGenerationOptions.Idempotent);
            await db.Database.ExecuteSqlRawAsync(script, ct);
            await db.Database.ExecuteSqlRawAsync(script, ct);
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(await ArchiveAsync(db, ct))));
            var menuError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"ShopMenuItems\" SET \"Price\"=0", ct));
            Assert.Equal(PostgresErrorCodes.CheckViolation, menuError.SqlState);
            var cityError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM \"Cities\"", ct));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, cityError.SqlState);
            // Old join inserts omit the additive columns; the compatibility trigger supplies CategoryId.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Equipments" ("Id","Brand","ModelName","Name","CategoryId","IsCustom","SlugRevision","CreatedAtUtc")
                VALUES ('00000000-0000-0000-0000-000000000005','Legacy','Second','Legacy Second',500,false,0,'2024-01-01T00:00:00Z');
                INSERT INTO "CoffeeShopEquipments" ("CoffeeShopId","EquipmentId")
                VALUES ('00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000005');
                """, ct);
            var legacy = await db.ShopEquipments.SingleAsync(a => a.EquipmentId == Guid.Parse("00000000-0000-0000-0000-000000000005"), ct);
            Assert.Equal(500, legacy.CategoryId); Assert.False(legacy.IsPrimary);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "CoffeeShopEquipments" SET "IsPrimary"=true WHERE "EquipmentId"='00000000-0000-0000-0000-000000000004'
                """, ct);
            var primaryError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("""
                UPDATE "CoffeeShopEquipments" SET "IsPrimary"=true WHERE "EquipmentId"='00000000-0000-0000-0000-000000000005'
                """, ct));
            Assert.Equal("UX_CoffeeShopEquipments_PrimaryCategory", primaryError.ConstraintName);
            Assert.Equal(3, await db.ShopEquipments.CountAsync(ct));
        }
        finally { await LivePostgresServer.DropDatabaseAsync(connection); }
    }

    private static Task SeedAsync(ShopsDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        INSERT INTO "Cities" ("Id","Name","SlugRevision","CreatedAtUtc")
        VALUES ('00000000-0000-0000-0000-000000000001','Historical city',0,'2024-01-01T00:00:00Z');
        INSERT INTO "Shops" ("Id","Name","PriceRange","Status","IsHidden","SlugRevision","CreatedAtUtc","CityId","Address","IsAddressValidated")
        SELECT id,'Historical shop',1,0,false,0,'2024-01-01T00:00:00Z','00000000-0000-0000-0000-000000000001','Historical address',false
        FROM (VALUES ('00000000-0000-0000-0000-000000000002'::uuid),('00000000-0000-0000-0000-000000000003'::uuid)) shops(id);
        INSERT INTO "EquipmentCategories" ("Id","Name","CreatedAtUtc") VALUES (500,'Historical machine','2024-01-01T00:00:00Z');
        INSERT INTO "Equipments" ("Id","Brand","ModelName","Name","CategoryId","IsCustom","IsPrimary","SlugRevision","CreatedAtUtc")
        VALUES ('00000000-0000-0000-0000-000000000004','Legacy','Shared','Legacy Shared',500,false,true,0,'2024-01-01T00:00:00Z');
        INSERT INTO "CoffeeShopEquipments" ("CoffeeShopId","EquipmentId")
        SELECT "Id",'00000000-0000-0000-0000-000000000004' FROM "Shops";
        INSERT INTO "ShopMenus" ("Id","CoffeeShopId","Currency","ParseStatus","CreatedAtUtc")
        VALUES ('00000000-0000-0000-0000-000000000006','00000000-0000-0000-0000-000000000002','BYN',0,'2024-01-01T00:00:00Z');
        INSERT INTO "ShopMenuItems" ("Id","ShopMenuId","DrinkDefinitionId","Availability","Source","Kind","Price","VolumeMl","CreatedAtUtc")
        SELECT '00000000-0000-0000-0000-000000000007','00000000-0000-0000-0000-000000000006',"Id",1,0,0,8,200,'2024-01-01T00:00:00Z'
        FROM "CoffeeDrinkDefinitions" ORDER BY "Id" LIMIT 1;
        """, ct);

    private static Task<string> ArchiveAsync(ShopsDbContext db, CancellationToken ct) => db.Database.SqlQueryRaw<string>("""
        SELECT jsonb_build_object(
            'cities',(SELECT jsonb_agg(to_jsonb(c) ORDER BY c."Id") FROM "Cities" c),
            'shops',(SELECT jsonb_agg(to_jsonb(s)-'ProfileRevision' ORDER BY s."Id") FROM "Shops" s),
            'equipment',(SELECT jsonb_agg(to_jsonb(e) ORDER BY e."Id") FROM "Equipments" e),
            'assignments',(SELECT jsonb_agg(to_jsonb(a)-'CategoryId'-'IsPrimary' ORDER BY a."CoffeeShopId",a."EquipmentId") FROM "CoffeeShopEquipments" a),
            'menus',(SELECT jsonb_agg(to_jsonb(m) ORDER BY m."Id") FROM "ShopMenus" m),
            'items',(SELECT jsonb_agg(to_jsonb(i)-'VolumeMl' ORDER BY i."Id") FROM "ShopMenuItems" i))::text AS "Value"
        """).SingleAsync(ct);
}
