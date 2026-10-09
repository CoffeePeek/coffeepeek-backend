-- Run against the Shops database before applying the city-reference FK migration.
-- Any returned row is an orphan. Review its real city, then explicitly restore that
-- Cities row or correct the individual reference. The migration will stop rather
-- than delete rows or silently choose a replacement city.
WITH orphan_references AS (
    SELECT 'Shops' AS source, s."Id" AS entity_id, s."CityId" AS city_id
    FROM "Shops" AS s
    LEFT JOIN "Cities" AS c ON c."Id" = s."CityId"
    WHERE s."CityId" IS NOT NULL AND c."Id" IS NULL

    UNION ALL

    SELECT 'Roasters', r."Id", r."CityId"
    FROM "Roasters" AS r
    LEFT JOIN "Cities" AS c ON c."Id" = r."CityId"
    WHERE r."CityId" IS NOT NULL AND c."Id" IS NULL

    UNION ALL

    SELECT 'CoffeeZones', z."Id", z."CityId"
    FROM "CoffeeZones" AS z
    LEFT JOIN "Cities" AS c ON c."Id" = z."CityId"
    WHERE z."CityId" IS NOT NULL AND c."Id" IS NULL
)
SELECT source, entity_id, city_id
FROM orphan_references
ORDER BY source, entity_id;
