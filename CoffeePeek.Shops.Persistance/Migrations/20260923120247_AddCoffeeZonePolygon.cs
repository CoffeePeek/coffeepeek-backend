using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeePeek.Shops.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class AddCoffeeZonePolygon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Polygon",
                table: "CoffeeZones",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]"); // EF scaffolds "{}"; a complex collection needs a JSON array or existing rows fail to load.

            // Zones are polygon-only now: turn each existing circle into the same 32-point outline
            // CoffeeZonePoint.Circle builds, so current memberships survive the switch.
            migrationBuilder.Sql("""
                UPDATE "CoffeeZones" z
                SET "Polygon" = (
                    SELECT jsonb_agg(jsonb_build_object(
                        'Latitude', round(z."CenterLatitude"
                            + (z."RadiusMeters" / 111320.0 * sin(2 * pi() * i / 32))::numeric, 10),
                        'Longitude', round(z."CenterLongitude"
                            + (z."RadiusMeters" / 111320.0 / cos(radians(z."CenterLatitude"::float8))
                               * cos(2 * pi() * i / 32))::numeric, 10)) ORDER BY i)
                    FROM generate_series(0, 31) AS i)
                WHERE jsonb_array_length(z."Polygon") = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Polygon",
                table: "CoffeeZones");
        }
    }
}
