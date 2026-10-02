START TRANSACTION;
ALTER TABLE "Reviews" ADD "DrinkSlug" character varying(50);

ALTER TABLE "Reviews" ADD "CustomDrinkName" character varying(100);

ALTER TABLE "Reviews" ADD "DrinkNameRu" character varying(100);

ALTER TABLE "Reviews" ADD "DrinkNameEn" character varying(100);

ALTER TABLE "CheckIns" ADD "DrinkSlug" character varying(50);

ALTER TABLE "CheckIns" ADD "CustomDrinkName" character varying(100);

ALTER TABLE "CheckIns" ADD "DrinkNameRu" character varying(100);

ALTER TABLE "CheckIns" ADD "DrinkNameEn" character varying(100);

INSERT INTO "CoffeeDrinkDefinitions" ("Id", "Slug", "NameRu", "NameEn", "Category", "Kind", "Aliases", "SortOrder", "IsActive", "CreatedAtUtc")
VALUES ('b1000000-0000-4000-8000-000000000015', 'lungo', 'Лунго', 'Lungo', 1, 1, 'лунго,lungo', 25, TRUE, TIMESTAMPTZ '2026-10-02T00:00:00Z');

UPDATE "CoffeeDrinkDefinitions"
SET "Aliases" = replace("Aliases", ',lungo', '')
WHERE "Slug" = 'americano';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261002131908_AddConsumedDrinks', '10.0.5');

COMMIT;

