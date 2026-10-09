using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class CityReferenceViolationTests
{
    [Theory]
    [InlineData("FK_Shops_Cities_CityId", true)]
    [InlineData("FK_Roasters_Cities_CityId", true)]
    [InlineData("FK_CoffeeZones_Cities_CityId", true)]
    [InlineData("FK_Other_Other", false)]
    public void OnlyCityForeignKeyViolationsAreMappedToConflict(string constraint, bool expected)
    {
        var pg = new PostgresException("reference error", "ERROR", "ERROR",
            PostgresErrorCodes.ForeignKeyViolation, constraintName: constraint);
        var wrapped = new DbUpdateException("save failed", pg);

        Assert.Equal(expected, CityReferenceViolation.Matches(wrapped));
    }
}
