using Npgsql;

namespace CoffeePeek.Shops.Persistance.Repositories;

public static class CityReferenceViolation
{
    public static bool Matches(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.ForeignKeyViolation,
                    ConstraintName: "FK_Shops_Cities_CityId" or "FK_Roasters_Cities_CityId" or "FK_CoffeeZones_Cities_CityId"
                })
                return true;
        }

        return false;
    }
}
