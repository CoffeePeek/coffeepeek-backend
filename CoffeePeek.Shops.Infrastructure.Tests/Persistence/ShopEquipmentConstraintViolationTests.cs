using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class ShopEquipmentConstraintViolationTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "UX_CoffeeShopEquipments_PrimaryCategory", true)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "FK_CoffeeShopEquipments_Equipments_EquipmentId_CategoryId", true)]
    [InlineData(PostgresErrorCodes.UniqueViolation, "UX_Other", false)]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "FK_Other", false)]
    public void MapsOnlyAssignmentConstraints(string sqlState, string constraint, bool expected)
    {
        var pg = new PostgresException("assignment conflict", "ERROR", "ERROR", sqlState,
            constraintName: constraint);
        Assert.Equal(expected, ShopEquipmentConstraintViolation.Matches(new DbUpdateException("save failed", pg)));
    }
}
