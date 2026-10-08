using Npgsql;

namespace CoffeePeek.Shops.Persistance.Repositories;

public static class ShopEquipmentConstraintViolation
{
    public static bool Matches(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: "UX_CoffeeShopEquipments_PrimaryCategory"
                })
                return true;

            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.ForeignKeyViolation,
                    ConstraintName: "FK_CoffeeShopEquipments_Equipments_EquipmentId_CategoryId"
                })
                return true;
        }

        return false;
    }
}
