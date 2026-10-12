using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations;

public partial class AssignShopEquipmentPrimary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep the old catalog column for a mixed-version window. The new model ignores it,
        // but an old binary still reads and writes it. New equipment rows need a DB default.
        migrationBuilder.Sql("ALTER TABLE \"Equipments\" ALTER COLUMN \"IsPrimary\" SET DEFAULT FALSE;");

        migrationBuilder.DropForeignKey(
            name: "FK_CoffeeShopEquipments_Equipments_EquipmentId",
            table: "CoffeeShopEquipments");
        migrationBuilder.DropIndex(
            name: "IX_CoffeeShopEquipments_EquipmentId",
            table: "CoffeeShopEquipments");

        migrationBuilder.AddColumn<int>(
            name: "CategoryId",
            table: "CoffeeShopEquipments",
            type: "integer",
            nullable: true);
        migrationBuilder.AddColumn<bool>(
            name: "IsPrimary",
            table: "CoffeeShopEquipments",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Old binaries insert join rows without CategoryId. Fill it before the NOT NULL and
        // composite FK checks; new writers may still provide it and the FK validates them.
        migrationBuilder.Sql(
            """
            CREATE FUNCTION "FillShopEquipmentCategory"()
            RETURNS trigger LANGUAGE plpgsql AS $trigger$
            BEGIN
                IF TG_OP = 'INSERT' AND NEW."CategoryId" IS NULL THEN
                    SELECT equipment."CategoryId" INTO NEW."CategoryId"
                    FROM "Equipments" AS equipment
                    WHERE equipment."Id" = NEW."EquipmentId";
                ELSIF TG_OP = 'UPDATE' THEN
                    IF NEW."CategoryId" IS NULL OR NEW."EquipmentId" IS DISTINCT FROM OLD."EquipmentId" THEN
                        SELECT equipment."CategoryId" INTO NEW."CategoryId"
                        FROM "Equipments" AS equipment
                        WHERE equipment."Id" = NEW."EquipmentId";
                    END IF;
                END IF;
                RETURN NEW;
            END
            $trigger$;

            CREATE TRIGGER "TR_CoffeeShopEquipments_FillCategory"
            BEFORE INSERT OR UPDATE OF "EquipmentId", "CategoryId"
            ON "CoffeeShopEquipments"
            FOR EACH ROW EXECUTE FUNCTION "FillShopEquipmentCategory"();
            """);

        migrationBuilder.Sql(
            """
            UPDATE "CoffeeShopEquipments" AS assignment
            SET "CategoryId" = equipment."CategoryId"
            FROM "Equipments" AS equipment
            WHERE assignment."EquipmentId" = equipment."Id";

            DO $preflight$
            BEGIN
                IF EXISTS (SELECT 1 FROM "CoffeeShopEquipments" WHERE "CategoryId" IS NULL) THEN
                    RAISE EXCEPTION
                        'Equipment assignment backfill failed: some equipment references have no category.';
                END IF;
            END
            $preflight$;
            """);

        migrationBuilder.AlterColumn<int>(
            name: "CategoryId",
            table: "CoffeeShopEquipments",
            type: "integer",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_Equipments_Id_CategoryId",
            table: "Equipments",
            columns: new[] { "Id", "CategoryId" });

        migrationBuilder.CreateIndex(
            name: "IX_CoffeeShopEquipments_EquipmentId_CategoryId",
            table: "CoffeeShopEquipments",
            columns: new[] { "EquipmentId", "CategoryId" });
        migrationBuilder.CreateIndex(
            name: "UX_CoffeeShopEquipments_PrimaryCategory",
            table: "CoffeeShopEquipments",
            columns: new[] { "CoffeeShopId", "CategoryId" },
            unique: true,
            filter: "\"IsPrimary\"");

        migrationBuilder.AddForeignKey(
            name: "FK_CoffeeShopEquipments_Equipments_EquipmentId_CategoryId",
            table: "CoffeeShopEquipments",
            columns: new[] { "EquipmentId", "CategoryId" },
            principalTable: "Equipments",
            principalColumns: new[] { "Id", "CategoryId" },
            onDelete: ReferentialAction.Cascade);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER "TR_CoffeeShopEquipments_FillCategory" ON "CoffeeShopEquipments";
            DROP FUNCTION "FillShopEquipmentCategory"();
            """);
        migrationBuilder.DropForeignKey(
            name: "FK_CoffeeShopEquipments_Equipments_EquipmentId_CategoryId",
            table: "CoffeeShopEquipments");
        migrationBuilder.DropIndex(
            name: "IX_CoffeeShopEquipments_EquipmentId_CategoryId",
            table: "CoffeeShopEquipments");
        migrationBuilder.DropIndex(
            name: "UX_CoffeeShopEquipments_PrimaryCategory",
            table: "CoffeeShopEquipments");
        migrationBuilder.DropUniqueConstraint(
            name: "AK_Equipments_Id_CategoryId",
            table: "Equipments");
        migrationBuilder.DropColumn("CategoryId", "CoffeeShopEquipments");
        migrationBuilder.DropColumn("IsPrimary", "CoffeeShopEquipments");

        migrationBuilder.CreateIndex(
            name: "IX_CoffeeShopEquipments_EquipmentId",
            table: "CoffeeShopEquipments",
            column: "EquipmentId");
        migrationBuilder.AddForeignKey(
            name: "FK_CoffeeShopEquipments_Equipments_EquipmentId",
            table: "CoffeeShopEquipments",
            column: "EquipmentId",
            principalTable: "Equipments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.Sql("ALTER TABLE \"Equipments\" ALTER COLUMN \"IsPrimary\" DROP DEFAULT;");
    }
}
